﻿namespace Models.Core
{
    using APSIM.Shared.Utilities;
    using Runners;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Collections;
    using System.Linq;

    /// <summary>
    /// Gets a run job for running one or more simulations.
    /// </summary>
    public class Runner
    {
        /// <summary>Runs the specified simulations.</summary>
        /// <param name="model">Simulations to run.</param>
        /// <param name="simulations">Simulations model.</param>
        /// <param name="runTests">Run the test nodes?</param>
        /// <returns>A runnable job or null if nothing to run.</returns>
        public static RunOrganiser ForSimulations(Simulations simulations, IModel model, bool runTests)
        {
            return new RunOrganiser(simulations, model, runTests);
        }

        /// <summary>Run simulations in files specified by a file specification.</summary>
        /// <param name="fileSpec">The file specification</param>
        /// <param name="recurse">Recurse throug sub directories?</param>
        /// <param name="runTests">Run the test nodes?</param>
        /// <returns>The file of jobs that were run.</returns>
        public static IJobManager ForFolder(string fileSpec, bool recurse, bool runTests)
        {
            return new RunDirectoryOfApsimFiles(fileSpec, recurse, runTests);
        }

        /// <summary>Run simulations in files specified by a file specification.</summary>
        /// <param name="fileName">The file specification</param>
        /// <param name="runTests">Run the test nodes?</param>
        /// <returns>The file of jobs that were run.</returns>
        public static RunOrganiser ForFile(string fileName, bool runTests)
        {
            if (!File.Exists(fileName))
                throw new Exception("Cannot find file: " + fileName);            
            Simulations simulations = Simulations.Read(fileName);            
            return ForSimulations(simulations, simulations, runTests);
        }

        /// <summary>Runs the specified simulations.</summary>
        /// <param name="underModel">Look at this model and all child models for simulations to create</param>
        /// <returns>A list of all created simulations</returns>
        public static SimulationCreator AllSimulations(IModel underModel)
        {
            return new SimulationCreator(underModel);
        }

        /// <summary>An enumable class for creating simulations ready for running.</summary>
        public class SimulationCreator : IEnumerable<Simulation>
        {
            private SimulationEnumerator simulations;

            /// <summary>Simulation names being run</summary>
            public List<string> SimulationNamesBeingRun { get { return simulations.SimulationNamesBeingRun; } }

            /// <summary>Constructor</summary>
            /// <param name="underModel">Look at this model and all child models for simulations to create</param>
            public SimulationCreator(IModel underModel)
            {
                simulations = new SimulationEnumerator(underModel);
            }

            /// <summary>Return simulation enumerator</summary>
            IEnumerator<Simulation> IEnumerable<Simulation>.GetEnumerator()
            {
                return simulations;
            }

            /// <summary>Return simulation enumerator</summary>
            IEnumerator IEnumerable.GetEnumerator()
            {
                return simulations;
            }
        }

        /// <summary>An enumerator for creating simulations ready for running.</summary>
        public class SimulationEnumerator : IEnumerator<Simulation>
        {
            private IModel relativeTo;
            private List<ISimulationGenerator> modelsToRun;
            private Simulation currentSimulation;

            /// <summary>Simulation names being run</summary>
            public List<string> SimulationNamesBeingRun { get; private set; }

            /// <summary>Constructor</summary>
            /// <param name="underModel">Look at this model and all child models for simulations to create</param>
            public SimulationEnumerator(IModel underModel)
            {
                relativeTo = underModel;
                FindListOfModelsToRun();
            }

            /// <summary>Return the current simulation</summary>
            Simulation IEnumerator<Simulation>.Current { get { return currentSimulation; } }

            /// <summary>Return the current simulation</summary>
            object IEnumerator.Current { get { return currentSimulation; } }

            /// <summary>Dispose of object</summary>
            void IDisposable.Dispose() { }

            /// <summary>Move to next simulation</summary>
            bool IEnumerator.MoveNext()
            {
                if (modelsToRun == null)
                    return false;
                else
                {
                    // Iterate through all jobs and return the next one.
                    currentSimulation = null;
                    if (modelsToRun.Count > 0)
                    {
                        currentSimulation = modelsToRun[0].NextSimulationToRun(false);
                        while (currentSimulation == null && modelsToRun.Count > 0)
                        {
                            modelsToRun.RemoveAt(0);
                            if (modelsToRun.Count > 0)
                                currentSimulation = modelsToRun[0].NextSimulationToRun(false);
                        }
                    }
                    return currentSimulation != null;
                }
            }

            /// <summary>Reset the enumerator</summary>
            void IEnumerator.Reset()
            {
                FindListOfModelsToRun();
            }

            /// <summary>Determine the list of jobs to run</summary>
            private void FindListOfModelsToRun()
            {
                // Get a list of all models we're going to run.
                modelsToRun = Apsim.ChildrenRecursively(relativeTo, typeof(ISimulationGenerator)).Cast<ISimulationGenerator>().ToList();
                if (relativeTo is ISimulationGenerator)
                    modelsToRun.Add(relativeTo as ISimulationGenerator);

                // For each model, resolve any links.
                Simulations sims = Apsim.Parent(relativeTo, typeof(Simulations)) as Simulations;
                modelsToRun.ForEach(model => sims.Links.Resolve(model));

                // For each model, get a list of simulation names.
                SimulationNamesBeingRun = new List<string>();
                modelsToRun.ForEach(model => SimulationNamesBeingRun.AddRange(model.GetSimulationNames(false)));
            }
        }

    }
}