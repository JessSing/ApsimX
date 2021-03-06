﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.IO;
using APSIM.Shared.Utilities;
using System.Data;

namespace Utility
{
    /// <summary>
    /// Class for executing arbitrary R code through APSIM.
    /// </summary>
    public class R
    {
        /// <summary>
        /// Path to a temporary working directory for the script.
        /// </summary>
        private string workingDirectory;

        /// <summary>
        /// Takes care of initialising and starting the process, reading output, etc.
        /// </summary>
        private ProcessUtilities.ProcessWithRedirectedOutput proc;

        /// <summary>
        /// Default constructor. Checks if R is installed, and prompts user to install it if not.
        /// </summary>
        public R()
        {
            if (ProcessUtilities.CurrentOS.IsWindows)
                InstallDirectory = GetRInstallDirectoryFromRegistry();
            else if (ProcessUtilities.CurrentOS.IsMac)
                throw new NotImplementedException();
            else if (ProcessUtilities.CurrentOS.IsLinux)
                throw new NotImplementedException();
        }

        /// <summary>
        /// Invoked when the R script is finished.
        /// </summary>
        public EventHandler Finished;

        /// <summary>
        /// Directory containing R executable.
        /// </summary>
        public string InstallDirectory { get; set; }

        /// <summary>
        /// Standard output generated by the script. 
        /// This value is not set until the script has finished running
        /// (when <see cref="Finished"/> is invoked).
        /// </summary>
        public string Output
        {
            get
            {
                return proc.StdOut;
            }
        }

        /// <summary>
        /// Standard error generated by the script
        /// This value is not set until the script has finished running
        /// (when <see cref="Finished"/> is invoked).
        /// </summary>
        public string Error
        {
            get
            {
                return proc.StdErr;
            }
        }

        /// <summary>
        /// Starts the execution of an R script.
        /// </summary>
        /// <param name="fileName">Path to an R script.</param>
        public void RunAsync(string fileName)
        {
            if (File.Exists(fileName))
            {
                string rScript = GetRExePath();
                // Create a temporary working directory.
                workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                if (!Directory.Exists(workingDirectory)) // I would be very suprised if it did already exist
                    Directory.CreateDirectory(workingDirectory);

                proc = new ProcessUtilities.ProcessWithRedirectedOutput();
                proc.Exited += OnExited;
                proc.Start(rScript, fileName + " ", workingDirectory, true);
            }
        }

        /// <summary>
        /// Runs an R script. UI will be unresponsive until the script finishes its execution.
        /// </summary>
        /// <param name="fileName">Path to an R script.</param>
        /// <returns>Standard output generated by the R script.</returns>
        public string Run(string fileName)
        {
            RunAsync(fileName);
            proc.WaitForExit();
            return Output;
        }

        /// <summary>
        /// Runs an R script (synchronously) and returns the stdout as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public DataTable RunToTable(string fileName)
        {
            // Not sure that this method really belongs in this class, but it can stay here for now.
            string result = Run(fileName);

            string tempFile = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "csv");
            if (!File.Exists(tempFile))
                File.Create(tempFile).Close();
            
            File.WriteAllText(tempFile, result);
            DataTable table = ApsimTextFile.ToTable(tempFile);
            System.Threading.Thread.Sleep(200);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            return table;
        }

        /// <summary>
        /// Kills the process running the R script.
        /// </summary>
        public void Kill()
        {
            proc.Kill();
        }

        /// <summary>
        /// Runs when the script has finished running.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnExited(object sender, EventArgs e)
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory);
            Finished?.Invoke(sender, e);
        }

        /// <summary>
        /// Get path to RScript.exe
        /// By default we try to use the 64-bit version.
        /// </summary>
        /// <returns>Path to RScript.exe</returns>
        private string GetRExePath()
        {
            string rScript;
            string rScript32 = Path.Combine(InstallDirectory, "bin", "Rscript.exe");
            string rScript64 = Path.Combine(InstallDirectory, "bin", "x64", "Rscript.exe");

            if (File.Exists(rScript64))
                rScript = rScript64;
            else if (File.Exists(rScript32))
                rScript = rScript32;
            else
                throw new Exception("Unable to find path to R binaries.");
            return rScript;
        }

        /// <summary>
        /// Gets the directory that the latest version of R is installed to.
        /// </summary>
        /// <param name="registryKey"></param>
        private string GetRInstallDirectoryFromRegistry()
        {
            string registryKey = @"SOFTWARE\R-core";
            List<string> subKeyNames = GetSubKeys(registryKey);

            string rKey;
            if (subKeyNames.Contains("R64"))
                rKey = registryKey + @"\R64";
            else if (subKeyNames.Contains("R"))
                rKey = registryKey + @"\R";
            else
                throw new Exception("R is not installed.");

            List<string> versions = GetSubKeys(rKey);

            // Ignore Microsoft R client. 
            string latestVersionKeyName = rKey + @"\" + versions.Where(v => !v.Contains("Microsoft R Client")).OrderByDescending(i => i).First();
            using (RegistryKey latestVersionKey = Registry.LocalMachine.OpenSubKey(latestVersionKeyName))
            {
                return Registry.GetValue(latestVersionKey.ToString(), "InstallPath", null) as string;
            }
        }

        /// <summary>
        /// Gets all sub keys of a given key name in the registry.
        /// </summary>
        /// <param name="keyName"></param>
        /// <returns></returns>
        private List<string> GetSubKeys(string keyName)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyName))
            {
                return key.GetSubKeyNames().ToList();
            }
        }
    }
}
