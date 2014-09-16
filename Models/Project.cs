﻿// <copyright file="Program.cs" company="Apsim"> The APSIM Initiative 2014. </copyright>

/// <summary>
/// Program Models.exe. The execution engine of apsim.
/// </summary>
namespace Models
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Text;
    using System.Xml;
    using Models.Core;

    /// <summary>
    /// Class to hold a static main entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args"> Command line arguments</param>
        /// <returns> Program exit code (0 for success)</returns>
        public static int Main(string[] args)
        {
            try
            {
                string fileName = null;
                string commandLineSwitch = null;

                // Check the command line arguments.
                if (args.Length >= 1)
                {
                    fileName = args[0];
                }

                if (args.Length == 2)
                {
                    commandLineSwitch = args[1];
                }

                if (args.Length < 1 || args.Length > 4)
                {
                    throw new Exception("Usage: ApsimX ApsimXFileSpec [/Recurse] [/Network] [/IP:<server IP>]");
                }

                Stopwatch timer = new Stopwatch();
                timer.Start();

                int numSimulations = 0;
                if (commandLineSwitch == "/SingleThreaded")
                {
                    numSimulations = RunSingleThreaded(fileName);
                }
                else if (args.Contains("/Network"))
                {
                    try
                    {
                        int indexOfIPArgument = -1;
                        for (int i = 0; i < args.Length; i++)
                        {
                            if (args[i].Contains("IP"))
                            {
                                indexOfIPArgument = i;
                                break;
                            }
                        }

                        if (indexOfIPArgument == -1)
                        {
                            throw new Exception("/Network specified, but no IP given (/IP:<server IP>]");
                        }

                        DoNetworkRun(fileName, args[indexOfIPArgument].Split(':')[1], args.Contains("/Recurse")); // send files over network
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("Connection to server terminated.");
                    }
                }
                else
                {
                    // Create a instance of a job that will go find .apsimx files. Then
                    // pass the job to a job runner.
                    RunDirectoryOfApsimFiles runApsim = new RunDirectoryOfApsimFiles();
                    runApsim.FileSpec = fileName;
                    runApsim.DoRecurse = args.Contains("/Recurse");
                    Utility.JobManager jobManager = new Utility.JobManager();
                    jobManager.OnComplete += OnError;
                    jobManager.AddJob(runApsim);
                    jobManager.Start(waitUntilFinished: true);
                    if (jobManager.SomeHadErrors)
                    {
                        Console.WriteLine("Errors found");
                        return 1;
                    }

                    // Write out the number of simulations run to the console.
                    numSimulations = jobManager.NumberOfJobs - 1;
                }

                timer.Stop();
                Console.WriteLine("Finished running " + numSimulations.ToString() + " simulations. Duration " + timer.Elapsed.TotalSeconds.ToString("#.00") + " sec.");
            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Run all simulations in the specified 'fileName' in a serial mode.
        /// i.e. don't use the multi-threaded JobManager. Useful for profiling.
        /// </summary>
        /// <param name="fileName"> the name of file to run </param>
        /// <returns> the number of simulations that were run</returns>
        private static int RunSingleThreaded(string fileName)
        {
            Simulations simulations = Simulations.Read(fileName);

            // Don't use JobManager - just run the simulations.
            Simulation[] simulationsToRun = Simulations.FindAllSimulationsToRun(simulations);
            foreach (Simulation simulation in simulationsToRun) 
            {
                simulation.Run(null, null);
            }

            return simulationsToRun.Length;
        }

        /// <summary>
        /// Get a list of files (apsimx, weather, input, etc) to send to remote computer.
        /// </summary>
        /// <param name="fileName">The file name to parse (Can include wildcard *).</param>
        /// <param name="doRecurse">Recurse through sub directories?</param>
        /// <returns>A list of file names.</returns>
        private static List<string> GetFileList(string fileName, bool doRecurse)
        {
            string[] files;
            List<string> fileList = new List<string>();

            if (fileName.Contains('*')) 
            {
                files = Directory.GetFiles(Path.GetDirectoryName(fileName), Path.GetFileName(fileName), doRecurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            } 
            else 
            {
                files = new string[] { fileName };
            }

            foreach (string s in files)
            {
                List<string> tempList = new List<string>();

                fileList.Add(s);
                XmlNodeList nodes;
                XmlDocument doc = new XmlDocument();

                doc.Load(s);
                XmlNode root = doc.DocumentElement;

                nodes = root.SelectNodes("//WeatherFile/FileName");
                foreach (XmlNode node in nodes) 
                {
                    tempList.Add(node.InnerText);
                }

                nodes = root.SelectNodes("//Model/FileName");
                foreach (XmlNode node in nodes) 
                {
                    tempList.Add(node.InnerText);
                }

                nodes = root.SelectNodes("//Input/FileNames/string");
                foreach (XmlNode node in nodes)
                {
                    tempList.Add(node.InnerText);
                }

                // resolve relative file name
                for (int i = 0; i < tempList.Count; i++)
                {
                    if (!File.Exists(tempList[i])) 
                    {
                        // Some of the unit tests are designed to fail so have invalid data
                        // They probably should go in a different directory.
                        // WHAT A SILLY IDEA! FIXME!!!!
                        if (s.Contains("UnitTests")) 
                        {
                            continue;
                        }

                        string tryFileName = Utility.PathUtils.GetAbsolutePath(tempList[i], s);
                        if (!File.Exists(tryFileName)) 
                        {
                            throw new ApsimXException(null, "Could not construct absolute path for " + tempList[i]);
                        }
                        else 
                        {
                            fileList.Add(tryFileName);
                        }
                    }
                }
            }
                
            return fileList;
        }

        /// <summary>
        /// Send our .apsimx and associated weather files over the network
        /// </summary>
        /// <param name="fileName">The .apsimx file to send.</param>
        /// <param name="serverIP">IP address of server to talk to </param>
        /// <param name="doRecurse">Recurse through sub directories?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1121:UseBuiltInTypeAlias", Justification = "Reviewed.")] 
        private static void DoNetworkRun(string fileName, string serverIP, bool doRecurse)
        {
            // get a list of files to send
            List<string> fileList = GetFileList(fileName, doRecurse);

            Console.WriteLine("Attempting connection to " + serverIP);
            using (TcpClient client = new TcpClient(serverIP, 50000))
            using (NetworkStream ns = client.GetStream())
            {
                Console.WriteLine("Connected to " + client.Client.RemoteEndPoint);

                // send the number of files to be sent
                byte[] dataLength = BitConverter.GetBytes(fileList.Count);
                ns.Write(dataLength, 0, 4);
                ns.Flush();

                foreach (string name in fileList)
                {
                    ApServer.Utility.SendNetworkFile(name, ns);
                }

                // get run updates from server
                byte[] msgLength = new byte[4];
                byte[] msg;
                while (true)
                {
                    ns.Read(msgLength, 0, 4);

                    // This test is the reason for the above warning suppression
                    if (BitConverter.ToInt32(msgLength, 0) == Int32.MaxValue) 
                    {
                        break;
                    }
                
                    msg = new byte[BitConverter.ToInt32(msgLength, 0)];
                    ns.Read(msg, 0, msg.Length);

                    Console.WriteLine(Encoding.ASCII.GetString(msg));
                }

                ApServer.Utility.ReceiveNetworkFiles(ns, client, Path.GetDirectoryName(fileName));
            }
        }

        /// <summary>
        /// When an error is encountered, this handler will be called.
        /// </summary>
        /// <param name="sender">The system object sending me this message</param>
        /// <param name="e">The error message</param>
        private static void OnError(object sender, Utility.JobManager.JobCompleteArgs e)
        {
            if (e.ErrorMessage != null)
            {
                Console.WriteLine(e.ErrorMessage);
            }
        }

        /// <summary>
        /// This runnable class finds .apsimx files on the 'fileSpec' passed into
        /// the constructor. If 'recurse' is true then it will also recursively
        /// look for files in sub directories.
        /// </summary>
        [Serializable]
        private class RunDirectoryOfApsimFiles : Utility.JobManager.IRunnable
        {
            /// <summary>
            /// Gets or sets the filespec that we will look for
            /// </summary>
            public string FileSpec { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether we search recursively for files matching 
            /// </summary>
            public bool DoRecurse { get; set; }

            /// <summary>
            /// Run this job.
            /// </summary>
            /// <param name="sender">A system telling us to go </param>
            /// <param name="e">Arguments to same </param>
            public void Run(object sender, System.ComponentModel.DoWorkEventArgs e)
            {
                // Extract the path from the filespec. If non specified then assume
                // current working directory.
                string path = Path.GetDirectoryName(this.FileSpec);
                if (path == null)
                {
                    path = Directory.GetCurrentDirectory();
                }

                List<string> files  = Directory.GetFiles(
                    path, 
                    Path.GetFileName(this.FileSpec), 
                    this.DoRecurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();

                // See above. FIXME!
                files.RemoveAll(s => s.Contains("UnitTests"));

                // Get a reference to the JobManager so that we can add jobs to it.
                Utility.JobManager jobManager = e.Argument as Utility.JobManager;

                // For each .apsimx file - read it in and create a job for each simulation it contains.
                bool errorsFound = false;
                foreach (string apsimxFileName in files)
                {
                    Simulations simulations = Simulations.Read(apsimxFileName);
                    if (simulations.LoadErrors.Count == 0)
                    {
                        jobManager.AddJob(simulations);
                    }
                    else
                    {
                        foreach (Exception err in simulations.LoadErrors)
                        {
                            Console.WriteLine(err.Message);
                            Console.WriteLine("Filename: " + apsimxFileName);
                            Console.WriteLine(err.StackTrace);
                            errorsFound = true;
                        }
                    }                    
                }
                
                if (errorsFound)
                {
                    // We've already outputted the load errors above. Just need to flag
                    // that an error has occurred.
                    throw new Exception(string.Empty); 
                }
            }
        }
    }
}