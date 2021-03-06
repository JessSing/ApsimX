﻿// -----------------------------------------------------------------------
// <copyright file="MainPresenter.cs"  company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------
namespace UserInterface.Presenters
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Xml;
    using APSIM.Shared.Utilities;
    using Importer;
    using Interfaces;
    using Models;
    using Models.Core;
    using Views;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// This presenter class provides the functionality behind a TabbedExplorerView 
    /// which is a tab control where each tabs represent an .apsimx file. Each tab
    /// then has an ExplorerPresenter and ExplorerView created when the tab is
    /// created.
    /// </summary>
    public class MainPresenter
    {
        /// <summary>A list of presenters for tabs on the left.</summary>
        public List<IPresenter> presenters1 = new List<IPresenter>();

        /// <summary>A private reference to the view this presenter will talk to.</summary>
        private IMainView view;

        /// <summary>The path last used to open the examples</summary>
        private string lastExamplesPath;

        /// <summary>A list of presenters for tabs on the right.</summary>
        private List<IPresenter> presenters2 = new List<IPresenter>();

        /// <summary>
        /// The most recent exception that has been thrown.
        /// </summary>
        public List<string> LastError { get; private set; }

        /// <summary>Attach this presenter with a view. Can throw if there are errors during startup.</summary>
        /// <param name="view">The view to attach</param>
        /// <param name="commandLineArguments">Optional command line arguments - can be null</param>
        public void Attach(object view, string[] commandLineArguments)
        {
            this.view = view as IMainView;

            // Set the main window location and size.
            this.view.WindowLocation = Utility.Configuration.Settings.MainFormLocation;
            this.view.WindowSize = Utility.Configuration.Settings.MainFormSize;
            this.view.WindowMaximised = Utility.Configuration.Settings.MainFormMaximized;

            // Set the main window caption with version information.
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version.Major == 0)
            {
                this.view.WindowCaption = "APSIM (Custom Build)";
            }
            else
            {
                this.view.WindowCaption = "APSIM " + version;
            }

            // Cleanup the recent file list
            Utility.Configuration.Settings.CleanMruList();

            // Populate the 2 start pages.
            this.PopulateStartPage(this.view.StartPage1);
            this.PopulateStartPage(this.view.StartPage2);

            // Trap some events.
            this.view.AllowClose += this.OnClosing;
            this.view.StartPage1.List.DoubleClicked += this.OnFileDoubleClicked;
            this.view.StartPage2.List.DoubleClicked += this.OnFileDoubleClicked;
            this.view.TabClosing += this.OnTabClosing;
            this.view.ShowDetailedError += this.ShowDetailedErrorMessage;
            this.view.Show();
            if (Utility.Configuration.Settings.StatusPanelHeight > 0.5 * this.view.WindowSize.Height)
                this.view.StatusPanelHeight = 20;
            else
                this.view.StatusPanelHeight = Utility.Configuration.Settings.StatusPanelHeight;
            // Process command line.
            this.ProcessCommandLineArguments(commandLineArguments);
        }

        /// <summary>Detach this presenter from the view.</summary>
        /// <param name="view">The view used for this object</param>
        public void Detach(object view)
        {
            this.view.AllowClose -= this.OnClosing;
            this.view.StartPage1.List.DoubleClicked -= this.OnFileDoubleClicked;
            this.view.StartPage2.List.DoubleClicked -= this.OnFileDoubleClicked;
            this.view.TabClosing -= this.OnTabClosing;
        }

        /// <summary>Allow the form to close?</summary>
        /// <returns>True if can be closed</returns>
        public bool AllowClose()
        {
            bool ok = true;

            foreach (ExplorerPresenter presenter in this.presenters1.OfType<ExplorerPresenter>())
            {
                ok = presenter.SaveIfChanged() && ok;
            }

            foreach (ExplorerPresenter presenter in this.presenters2.OfType<ExplorerPresenter>())
            {
                ok = presenter.SaveIfChanged() && ok;
            }

            return ok;
        }

        /// <summary>Toggle split screen view.</summary>
        public void ToggleSecondExplorerViewVisible()
        {
            this.view.SplitWindowOn = !this.view.SplitWindowOn;
        }

        /// <summary>Execute the specified script, returning any error messages or NULL if all OK.</summary>
        /// <param name="code">The script code</param>
        /// <returns>Any exception message or null</returns>
        public string ProcessStartupScript(string code)
        {
            Assembly compiledAssembly = ReflectionUtilities.CompileTextToAssembly(code, null);

            // Get the script 'Type' from the compiled assembly.
            Type scriptType = compiledAssembly.GetType("Script");
            if (scriptType == null)
            {
                throw new Exception("Cannot find a public class called 'Script'");
            }

            // Look for a method called Execute
            MethodInfo executeMethod = scriptType.GetMethod("Execute");
            if (executeMethod == null)
            {
                throw new Exception("Cannot find a method Script.Execute");
            }

            // Create a new script model.
            object script = compiledAssembly.CreateInstance("Script");

            // Call Execute on our newly created script instance.
            object[] arguments = new object[] { this };
            try
            {
                executeMethod.Invoke(script, arguments);
            }
            catch (System.Reflection.TargetInvocationException except)
            {
                return except.InnerException.ToString();
            }

            return null;
        }

        /// <summary>
        /// Add a status message. A message of null will clear the status message.
        /// For error messages, use <see cref="ShowError(Exception)"/>.
        /// </summary>
        /// <param name="message">The message test</param>
        /// <param name="errorLevel">The error level value</param>
        public void ShowMessage(string message, Simulation.MessageType messageType)
        {
            Simulation.ErrorLevel errorType = Simulation.ErrorLevel.Information;

            if (messageType == Simulation.MessageType.Information)
                errorType = Simulation.ErrorLevel.Information;
            else if (messageType == Simulation.MessageType.Warning)
                errorType = Simulation.ErrorLevel.Warning;

            this.view.ShowMessage(message, errorType);
        }
        
        /// <summary>
        /// Displays several messages, with a separator between them. 
        /// For error messages, use <see cref="ShowError(List{Exception})"/>.
        /// </summary>
        /// <param name="messages">Messages to be displayed.</param>
        /// <param name="messageType"></param>
        public void ShowMessage(List<string> messages, Simulation.MessageType messageType)
        {
            Simulation.ErrorLevel errorType = Simulation.ErrorLevel.Information;

            if (messageType == Simulation.MessageType.Information)
                errorType = Simulation.ErrorLevel.Information;
            else if (messageType == Simulation.MessageType.Warning)
                errorType = Simulation.ErrorLevel.Warning;

            foreach (string msg in messages)
            {
                view.ShowMessage(msg, errorType, false, true, false);
            }
        }

        /// <summary>
        /// Displays a simple error message in the status bar, without a 'more information' button.
        /// If you've just caught an exception, <see cref="ShowError(Exception)"/> is probably a better method to use.
        /// </summary>
        /// <param name="error"></param>
        public void ShowError(string error)
        {
            LastError = new List<string>();
            view.ShowMessage(error, Simulation.ErrorLevel.Error, withButton : false);
        }

        /// <summary>
        /// Displays an error message in the status bar, along with a button which brings up more info.
        /// </summary>
        /// <param name="error">The error to be displayed.</param>
        public void ShowError(Exception error)
        {
            if (error != null)
            {
                LastError = new List<string> { error.ToString() };
                view.ShowMessage(error.Message, Simulation.ErrorLevel.Error);
            }
            else
            {
                LastError = new List<string>();
                ShowError(new NullReferenceException("Attempted to display a null error"));
            }
        }

        /// <summary>
        /// Displays several error messages in the status bar. Each error will have an associated 
        /// </summary>
        /// <param name="errors"></param>
        public void ShowError(List<Exception> errors)
        {
            // if the list contains at least 1 null exception, display none of them
            if (errors != null && !errors.Contains(null))
            {
                LastError = errors.Select(err => err.ToString()).ToList();
                for (int i = 0; i < errors.Count; i++)
                {
                    // only overwrite other messages the first time through the loop
                    view.ShowMessage(errors[i].Message, Simulation.ErrorLevel.Error, i == 0, true);
                }
            }
            else
            {
                LastError = new List<string>();
                ShowError(new NullReferenceException("Attempted to display a null error"));
            }
        }

        /// <summary>
        /// Shows a window which contains detailed error information (such as stack trace).
        /// </summary>
        /// <param name="sender">Sender object. Must be an <see cref="ApsimNG.Classes.CustomButton"/></param>
        /// <param name="e">Event Arguments.</param>
        private void ShowDetailedErrorMessage(object sender, EventArgs e)
        {
            if (sender is ApsimNG.Classes.CustomButton)
            {
                ErrorView err = new ErrorView(LastError[(sender as ApsimNG.Classes.CustomButton).ID], view as ViewBase);
                err.Show();
            }
        }

        /// <summary>Show a message in a dialog box</summary>
        /// <param name="message">The message.</param>
        /// <param name="title">The dialog title</param>
        /// <param name="msgType">The type of dialog message</param>
        /// <param name="buttonType">Button type</param>
        /// <returns>The message dialog return value</returns>
        public int ShowMsgDialog(string message, string title, Gtk.MessageType msgType, Gtk.ButtonsType buttonType)
        {
            return this.view.ShowMsgDialog(message, title, msgType, buttonType);
        }

        /// <summary>
        /// Show progress bar with the specified percent.
        /// </summary>
        /// <param name="percent">The progress</param>
        public void ShowProgress(int percent, bool showStopButton = true)
        {
            this.view.ShowProgress(percent, showStopButton);
        }

        /// <summary>
        /// Add a handler for the "stop" button of the view
        /// </summary>
        /// <param name="handler">The handler to be added</param>
        public void AddStopHandler(EventHandler<EventArgs> handler)
        {
            this.view.StopSimulation += handler;
        }

        /// <summary>
        /// Remove a handler for the "stop" button of the view
        /// </summary>
        /// <param name="handler">The handler to be removed</param>
        public void RemoveStopHandler(EventHandler<EventArgs> handler)
        {
            this.view.StopSimulation -= handler;
        }

        /// <summary>Show the wait cursor</summary>
        /// <param name="wait">If true will show the wait cursor otherwise the normal cursor.</param>
        public void ShowWaitCursor(bool wait)
        {
            this.view.ShowWaitCursor(wait);
        }

        /// <summary>Change the text of a tab.</summary>
        /// <param name="ownerView">The owning view</param>
        /// <param name="newTabName">New text of the tab.</param>
        /// <param name="tooltip">The tooltip text</param>
        public void ChangeTabText(object ownerView, string newTabName, string tooltip)
        {
            this.view.ChangeTabText(ownerView, newTabName, tooltip);
        }

        /// <summary>Close the application</summary>
        /// <param name="askToSave">Prompt to save</param>
        public void Close(bool askToSave)
        {
            this.view.Close(askToSave);
        }

        /// <summary>Ask the user a question</summary>
        /// <param name="message">The message to show the user.</param>
        /// <returns>A response value</returns>
        public QuestionResponseEnum AskQuestion(string message)
        {
            return this.view.AskQuestion(message);
        }

        /// <summary>Ask user for a filename to open.</summary>
        /// <param name="fileSpec">The file specification to use to filter the files.</param>
        /// <param name="initialDirectory">Optional Initial starting directory</param>
        /// <returns>A filename</returns>
        public string AskUserForOpenFileName(string fileSpec, string initialDirectory = "")
        {
            return this.view.AskUserForOpenFileName(fileSpec, initialDirectory);
        }

        /// <summary>
        /// A helper function that asks user for a SaveAs name and returns their new choice.
        /// </summary>
        /// <param name="fileSpec">The file specification to filter the files.</param>
        /// <param name="oldFilename">The current file name.</param>
        /// <returns>Returns the new file name or null if action cancelled by user.</returns>
        public string AskUserForSaveFileName(string fileSpec, string oldFilename)
        {
            return this.view.AskUserForSaveFileName(fileSpec, oldFilename);
        }

        /// <summary>Open an .apsimx file into the current tab.</summary>
        /// <param name="fileName">The file to open</param>
        /// <param name="onLeftTabControl">If true a tab will be added to the left hand tab control.</param>
        /// <returns>The presenter</returns>
        public ExplorerPresenter OpenApsimXFileInTab(string fileName, bool onLeftTabControl)
        {
            ExplorerPresenter presenter = null;
            if (fileName != null)
            {
                presenter = this.PresenterForFile(fileName, onLeftTabControl);
                if (presenter != null)
                {
                    this.view.SelectTabContaining(presenter.GetView().MainWidget);
                    return presenter;
                }

                this.view.ShowWaitCursor(true);
                try
                {
                    Simulations simulations = Simulations.Read(fileName);
                    presenter = (ExplorerPresenter)this.CreateNewTab(fileName, simulations, onLeftTabControl, "UserInterface.Views.ExplorerView", "UserInterface.Presenters.ExplorerPresenter");
                    if (simulations.LoadErrors.Count > 0)
                    {
                        ShowError(simulations.LoadErrors);
                    }

                    // Add to MRU list and update display
                    Utility.Configuration.Settings.AddMruFile(fileName);
                    this.UpdateMRUDisplay();
                }
                catch (Exception err)
                {
                    ShowError(err);
                }

                this.view.ShowWaitCursor(false);
            }

            return presenter;
        }

        /// <summary>
        /// Updates display of the list of most-recently-used files
        /// </summary>
        public void UpdateMRUDisplay()
        {
            this.view.StartPage1.List.Values = Utility.Configuration.Settings.MruList.ToArray();
            this.view.StartPage2.List.Values = Utility.Configuration.Settings.MruList.ToArray();
            Utility.Configuration.Settings.Save();
        }

        /// <summary>Event handler invoked when user clicks on 'Standard toolbox'</summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        public void OnStandardToolboxClick(object sender, EventArgs e)
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApsimNG.Resources.Toolboxes.StandardToolbox.apsimx");
            StreamReader streamReader = new StreamReader(s);
            bool onLeftTabControl = true;
            if (sender != null)
            {
                onLeftTabControl = this.view.IsControlOnLeft(sender);
            }

            this.OpenApsimXFromMemoryInTab("Standard toolbox", streamReader.ReadToEnd(), onLeftTabControl);
        }

        /// <summary>
        /// Closes the tab containing a specified object
        /// </summary>
        /// <param name="o">The object (normally a Gtk Widget) being sought</param>
        public void CloseTabContaining(object o)
        {
            this.view.CloseTabContaining(o);
        }

        /// <summary>Populate the view for the first time. Will throw if there are errors on startup.</summary>
        /// <param name="startPage">The start page to populate.</param>
        private void PopulateStartPage(IListButtonView startPage)
        {
            // Add the buttons into the main window.
            startPage.AddButton(
                                "Open APSIM File",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.OpenFile.png"),
                                          this.OnOpenApsimXFile);

            startPage.AddButton(
                                "Open an example",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.OpenExample.png"),
                                          this.OnExample);

            startPage.AddButton(
                                "Standard toolbox",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.Toolbox.png"),
                                          this.OnStandardToolboxClick);

            startPage.AddButton(
                                "Management toolbox",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.Toolbox.png"),
                                          this.OnManagementToolboxClick);

            startPage.AddButton(
                                "Training toolbox",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.Toolbox.png"),
                                          this.OnTrainingToolboxClick);

            startPage.AddButton(
                                "Import old .apsim file",
                                          new Gtk.Image(null, "ApsimNG.Resources.Toolboxes.Import.png"),
                                          this.OnImport);

            startPage.AddButton(
                                "Upgrade",
                                        new Gtk.Image(null, "ApsimNG.Resources.MenuImages.Upgrade.png"),
                                        this.OnUpgrade);
            startPage.AddButton(
                                "View Cloud Jobs",
                                        new Gtk.Image(null, "ApsimNG.Resources.Cloud.png"),
                                        this.OnViewCloudJobs);
            
            // Populate the view's listview.
            startPage.List.Values = Utility.Configuration.Settings.MruList.ToArray();

            this.PopulatePopup(startPage);
        }

        /// <summary>
        /// Defines the list of items to be included in a popup menu for the
        /// most-recently-used file display
        /// </summary>
        /// <param name="startPage">The page to which the menu will be added</param>
        private void PopulatePopup(IListButtonView startPage)
        {
            List<MenuDescriptionArgs> descriptions = new List<MenuDescriptionArgs>();
            MenuDescriptionArgs descOpen = new MenuDescriptionArgs();
            descOpen.Name = "Open";
            descOpen.Enabled = true;
            descOpen.OnClick = this.OnOpen;
            descriptions.Add(descOpen);

            MenuDescriptionArgs descRemove = new MenuDescriptionArgs();
            descRemove.Name = "Remove from recent file list";
            descRemove.Enabled = true;
            descRemove.OnClick = this.OnRemove;
            descriptions.Add(descRemove);

            MenuDescriptionArgs descClear = new MenuDescriptionArgs();
            descClear.Name = "Clear recent file list";
            descClear.Enabled = true;
            descClear.OnClick = this.OnClear;
            descriptions.Add(descClear);

            MenuDescriptionArgs descRename = new MenuDescriptionArgs();
            descRename.Name = "Rename";
            descRename.Enabled = true;
            descRename.OnClick = this.OnRename;
            descriptions.Add(descRename);

            MenuDescriptionArgs descCopy = new MenuDescriptionArgs();
            descCopy.Name = "Copy";
            descCopy.Enabled = true;
            descCopy.OnClick = this.OnCopy;
            descriptions.Add(descCopy);

            MenuDescriptionArgs descDelete = new MenuDescriptionArgs();
            descDelete.Name = "Delete";
            descDelete.Enabled = true;
            descDelete.OnClick = this.OnDelete;
            descriptions.Add(descDelete);

            startPage.List.PopulateContextMenu(descriptions);
        }

        /// <summary>
        /// Handles the Open menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnOpen(object obj, EventArgs args)
        {
            string fileName = this.view.GetMenuItemFileName(obj);
            if (fileName != null)
            {
                this.OpenApsimXFileInTab(fileName, this.view.IsControlOnLeft(obj));
                Utility.Configuration.Settings.PreviousFolder = Path.GetDirectoryName(fileName);
            }
        }

        /// <summary>
        /// Handles the Remove menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnRemove(object obj, EventArgs args)
        {
            string fileName = this.view.GetMenuItemFileName(obj);
            if (!string.IsNullOrEmpty(fileName))
            {
                Utility.Configuration.Settings.DelMruFile(fileName);
                this.UpdateMRUDisplay();
            }
        }

        /// <summary>
        /// Handles the Clear menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnClear(object obj, EventArgs args)
        {
            if (this.AskQuestion("Are you sure you want to completely clear the list of recently used files?") == QuestionResponseEnum.Yes)
            {
                string[] mruFiles = Utility.Configuration.Settings.MruList.ToArray();
                foreach (string fileName in mruFiles)
                {
                    Utility.Configuration.Settings.DelMruFile(fileName);
                }

                this.UpdateMRUDisplay();
            }
        }

        /// <summary>
        /// Handles the Rename menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnRename(object obj, EventArgs args)
        {
            string fileName = this.view.GetMenuItemFileName(obj);
            if (!string.IsNullOrEmpty(fileName))
            {
                string newName = this.AskUserForSaveFileName("ApsimX files|*.apsimx", fileName);
                if (!string.IsNullOrEmpty(newName) && newName != fileName)
                {
                    try
                    {
                        File.Move(fileName, newName);
                        Utility.Configuration.Settings.RenameMruFile(fileName, newName);
                        this.UpdateMRUDisplay();
                    }
                    catch (Exception e)
                    {
                        ShowError(new Exception("Error renaming file!", e));
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Copy menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnCopy(object obj, EventArgs args)
        {
            string fileName = this.view.GetMenuItemFileName(obj);
            if (!string.IsNullOrEmpty(fileName))
            {
                string newFileName = "Copy of " + Path.GetFileName(fileName);
                string newFilePath = Path.Combine(Path.GetDirectoryName(fileName), newFileName);
                string copyName = this.AskUserForSaveFileName("ApsimX files|*.apsimx", newFilePath);
                if (!string.IsNullOrEmpty(copyName))
                {
                    try
                    {
                        File.Copy(fileName, copyName);
                        Utility.Configuration.Settings.AddMruFile(copyName);
                        this.UpdateMRUDisplay();
                    }
                    catch (Exception e)
                    {
                        ShowError(new Exception("Error creating copy of file!", e));
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Delete menu item of the MRU context menu
        /// </summary>
        /// <param name="obj">The object issuing the event</param>
        /// <param name="args">Event parameters</param>
        private void OnDelete(object obj, EventArgs args)
        {
            string fileName = this.view.GetMenuItemFileName(obj);
            if (!string.IsNullOrEmpty(fileName))
            {
                if (this.AskQuestion("Are you sure you want to completely delete the file " + fileName + "?") == QuestionResponseEnum.Yes)
                {
                    try
                    {
                        File.Delete(fileName);
                        Utility.Configuration.Settings.DelMruFile(fileName);
                        this.UpdateMRUDisplay();
                    }
                    catch (Exception e)
                    {
                        ShowError(new Exception("Error deleting file!", e));
                    }
                }
            }
        }

        /// <summary>Process the specified command line arguments. Will throw if there are errors during startup.</summary>
        /// <param name="commandLineArguments">Optional command line arguments - can be null</param>
        private void ProcessCommandLineArguments(string[] commandLineArguments)
        {
            // Look for a script specified on the command line.
            foreach (string argument in commandLineArguments)
            {
                if (Path.GetExtension(argument) == ".cs")
                {
                    string result = this.ProcessStartupScript(File.ReadAllText(argument));
                    if (!string.IsNullOrEmpty(result))
                    {
                        throw new Exception(result);
                    }
                }
                else if (Path.GetExtension(argument) == ".apsimx")
                {
                    this.OpenApsimXFileInTab(argument, onLeftTabControl: true);
                }
            }
        }

        /// <summary>
        /// Returns the ExplorerPresenter for the specified file name, 
        /// or null if the file is not currently open
        /// </summary>
        /// <param name="fileName">The file name being sought</param>
        /// <param name="onLeftTabControl">If true, search the left screen, else search the right</param>
        /// <returns>The explorer presenter</returns>
        private ExplorerPresenter PresenterForFile(string fileName, bool onLeftTabControl)
        {            
            List<ExplorerPresenter> presenters = onLeftTabControl ? this.presenters1.OfType<ExplorerPresenter>().ToList() : this.presenters2.OfType<ExplorerPresenter>().ToList();
            foreach (ExplorerPresenter presenter in presenters)
            {
                if (presenter.ApsimXFile.FileName == fileName)
                {
                    return presenter;
                }
            }

            return null;
        }

        /// <summary>Open an .apsimx file into the current tab.</summary>
        /// <param name="name">Name of the simulation</param>
        /// <param name="contents">The xml content</param>
        /// <param name="onLeftTabControl">If true a tab will be added to the left hand tab control.</param>
        private void OpenApsimXFromMemoryInTab(string name, string contents, bool onLeftTabControl)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(contents);
            Simulations simulations = Simulations.Read(doc.DocumentElement);
            this.CreateNewTab(name, simulations, onLeftTabControl, "UserInterface.Views.ExplorerView", "UserInterface.Presenters.ExplorerPresenter");
        }

        /// <summary>Create a new tab.</summary>
        /// <param name="name">Name of the simulation</param>
        /// <param name="simulations">The simulations object to add to tab.</param>
        /// <param name="onLeftTabControl">If true a tab will be added to the left hand tab control.</param>
        /// <param name="viewName">Name of the view to create.</param>
        /// <param name="presenterName">Name of the presenter to create.</param>
        /// <returns>The explorer presenter</returns>
        private IPresenter CreateNewTab(string name, Simulations simulations, bool onLeftTabControl, string viewName, string presenterName)
        {
            this.view.ShowMessage(" ", Simulation.ErrorLevel.Information); // Clear the message window
            ViewBase newView;
            IPresenter newPresenter;
            try
            {
                newView = (ViewBase)Assembly.GetExecutingAssembly().CreateInstance(viewName, false, BindingFlags.Default, null, new object[] { this.view }, null, null);
                newPresenter = (IPresenter)Assembly.GetExecutingAssembly().CreateInstance(presenterName, false, BindingFlags.Default, null, new object[] { this }, null, null);
            }
            catch (InvalidCastException e)
            {
                // viewName or presenterName does not define a class derived from ViewBase or IPresenter, respectively.
                ShowError(e);
                return null;
            }
            
            //ExplorerView explorerView = new ExplorerView(null);
            //ExplorerPresenter presenter = new ExplorerPresenter(this);
            if (onLeftTabControl)
            {
                this.presenters1.Add(newPresenter);
            }
            else
            {
                this.presenters2.Add(newPresenter);
            }

            XmlDocument doc = new XmlDocument();
            newPresenter.Attach(simulations, newView, null);

            this.view.AddTab(name, null, newView.MainWidget, onLeftTabControl);

            // restore the simulation tree width on the form
            if (newPresenter.GetType() == typeof(ExplorerPresenter))
            {
                if (simulations.ExplorerWidth == 0)
                {
                    ((ExplorerPresenter)newPresenter).TreeWidth = 250;
                }
                else
                {
                    ((ExplorerPresenter)newPresenter).TreeWidth = simulations.ExplorerWidth;
                }
            }
            return newPresenter;
        }

        /// <summary>
        /// Event handler invoked when user clicks on 'Open ApsimX file'
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event parameters</param>
        private void OnOpenApsimXFile(object sender, EventArgs e)
        {
            string fileName = this.view.AskUserForOpenFileName("*.apsimx|*.apsimx");
            if (fileName != null)
            {
                bool onLeftTabControl = this.view.IsControlOnLeft(sender);
                this.OpenApsimXFileInTab(fileName, onLeftTabControl);
                Utility.Configuration.Settings.PreviousFolder = Path.GetDirectoryName(fileName);
            }
        }

        /// <summary>
        /// Open a recently used file
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnFileDoubleClicked(object sender, EventArgs e)
        {
            bool onLeftTabControl = this.view.IsControlOnLeft(sender);
            string fileName = onLeftTabControl ? this.view.StartPage1.List.SelectedValue : this.view.StartPage2.List.SelectedValue;
            if (fileName != null)
            {
                this.OpenApsimXFileInTab(fileName, onLeftTabControl);
                Utility.Configuration.Settings.PreviousFolder = Path.GetDirectoryName(fileName);
            }
        }

        /// <summary>
        /// Current tab is closing - remove presenter from our presenters list
        /// </summary>
        /// <param name="sender">Sender of event</param>
        /// <param name="e">Event arguments</param>
        private void OnTabClosing(object sender, TabClosingEventArgs e)
        {
            if (e.LeftTabControl)
            {
                IPresenter presenter = presenters1[e.Index - 1];
                e.AllowClose = true;
                if (presenter.GetType() == typeof(ExplorerPresenter)) e.AllowClose = ((ExplorerPresenter)presenter).SaveIfChanged();
                if (e.AllowClose)
                {
                    presenter.Detach();
                    this.presenters1.RemoveAt(e.Index - 1);
                }
            }
            else
            {
                IPresenter presenter = presenters2[e.Index - 1];
                e.AllowClose = true;
                if (presenter.GetType() == typeof(ExplorerPresenter)) e.AllowClose = ((ExplorerPresenter)presenter).SaveIfChanged();                
                if (e.AllowClose)
                {
                    presenter.Detach();
                    this.presenters2.RemoveAt(e.Index - 1);
                }
            }

            // We've just closed Simulations
            // This is a good time to force garbage collection 
            GC.Collect(2, GCCollectionMode.Forced, true);
        }

        /// <summary>
        /// Event handler invoked when user clicks on 'Management toolbox'
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnManagementToolboxClick(object sender, EventArgs e)
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApsimNG.Resources.Toolboxes.ManagementToolbox.apsimx");
            StreamReader streamReader = new StreamReader(s);
            bool onLeftTabControl = this.view.IsControlOnLeft(sender);
            this.OpenApsimXFromMemoryInTab("Management toolbox", streamReader.ReadToEnd(), onLeftTabControl);
        }

        /// <summary>
        /// Event handler invoked when user clicks on 'Training toolbox'
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnTrainingToolboxClick(object sender, EventArgs e)
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApsimNG.Resources.Toolboxes.TrainingToolbox.apsimx");
                StreamReader streamReader = new StreamReader(s);
                bool onLeftTabControl = this.view.IsControlOnLeft(sender);
                this.OpenApsimXFromMemoryInTab("Training toolbox", streamReader.ReadToEnd(), onLeftTabControl);
            }
            catch (Exception err)
            {
                string message = err.Message;
                if (err.InnerException != null)
                {
                    message += "\r\n" + err.InnerException.Message;
                }

                ShowError(err);
            }
        }

        /// <summary>
        /// Event handler invoked when user clicks on 'Import'
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnImport(object sender, EventArgs e)
        {
            string fileName = this.view.AskUserForOpenFileName("*.apsim|*.apsim");

            APSIMImporter importer = new APSIMImporter();
            try
            {
                this.view.ShowWaitCursor(true);
                try
                {
                    importer.ProcessFile(fileName);

                    string newFileName = Path.ChangeExtension(fileName, ".apsimx");
                    bool onLeftTabControl = this.view.IsControlOnLeft(sender);
                    this.OpenApsimXFileInTab(newFileName, onLeftTabControl);
                }
                finally
                {
                    this.view.ShowWaitCursor(false);
                }
            }
            catch (Exception exp)
            {
                throw new Exception("Failed import: " + exp.Message);
            }
        }

        /// <summary>
        /// Open a tab which shows a list of jobs submitted to the cloud.
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event Arguments</param>
        public void OnViewCloudJobs(object sender, EventArgs e)
        {
            bool onLeftTabControl = view.IsControlOnLeft(sender);            
            // Clear the message window
            view.ShowMessage(" ", Simulation.ErrorLevel.Information);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                CreateNewTab("View Cloud Jobs", null, onLeftTabControl, "UserInterface.Views.CloudJobDisplayView", "UserInterface.Presenters.AzureJobDisplayPresenter");
            } else
            {
                ShowError("Microsoft Azure functionality is currently only available under Windows.");
            }
            
        }

        /// <summary>
        /// Open a file open dialog with the initial directory in an Examples directory.
        /// Use one that is at the same level as this app directory.
        /// Any files opened here will need to be saved before running.
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnExample(object sender, EventArgs e)
        {
            string initialPath;

            if ((this.lastExamplesPath != null) && (this.lastExamplesPath.Length > 0) && Directory.Exists(this.lastExamplesPath))
            {
                initialPath = this.lastExamplesPath; // use the last used path in this session
            }
            else
            {
                // use an examples directory relative to this assembly
                initialPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                initialPath = Path.GetFullPath(Path.Combine(initialPath, "..", "Examples"));
            }

            string fileName = this.view.AskUserForOpenFileName("*.apsimx|*.apsimx", initialPath);

            if (fileName != null)
            {
                this.lastExamplesPath = Path.GetDirectoryName(fileName);

                // ensure that they are saved in another file before running by opening them in memory
                StreamReader reader = new StreamReader(fileName);
                bool onLeftTabControl = this.view.IsControlOnLeft(sender);
                string label = Path.GetFileNameWithoutExtension(fileName) + " (example)";
                this.OpenApsimXFromMemoryInTab(label, reader.ReadToEnd(), onLeftTabControl);
                reader.Close();
            }
        }

        /// <summary>
        /// Upgrade Apsim Next Generation
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnUpgrade(object sender, EventArgs e)
        {
            // Get the version of the current assembly.
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version.Revision == 0)
            {
                ShowError("You are on a custom build. You cannot upgrade.");
            }
            else
            {
                if (this.AllowClose())
                {
                    Forms.UpgradeForm form = new Forms.UpgradeForm(this.view);
                    form.Show();
                }
            }
        }

        /// <summary>Application is closing - allow this to happen?</summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Close arguments</param>
        private void OnClosing(object sender, AllowCloseArgs e)
        {
            e.AllowClose = this.AllowClose();
            if (e.AllowClose)
            {
                Utility.Configuration.Settings.MainFormLocation = this.view.WindowLocation;
                Utility.Configuration.Settings.MainFormSize = this.view.WindowSize;
                Utility.Configuration.Settings.MainFormMaximized = this.view.WindowMaximised;
                Utility.Configuration.Settings.StatusPanelHeight = this.view.StatusPanelHeight;
            }
        }
    }
}
