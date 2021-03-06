// ----------------------------------------------------------------------
// <copyright file="JobRunnerAsync.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
//-----------------------------------------------------------------------
namespace APSIM.Shared.Utilities
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>A class for running jobs asynchronously.</summary>
    public class JobRunnerAsync : IJobRunner
    {
        /// <summary>A token for cancelling running of jobs</summary>
        private CancellationTokenSource cancelToken;

        /// <summary>Number of tasks currently running</summary>
        private int numberTasksRunning;

        /// <summary>Occurs when all jobs completed.</summary>
        public event EventHandler<AllCompletedArgs> AllJobsCompleted;

        /// <summary>Invoked when a job is completed.</summary>
        public event EventHandler<JobCompleteArgs> JobCompleted;

        /// <summary>Run the specified jobs</summary>
        /// <param name="jobs">An instance of a class that manages all jobs.</param>
        /// <param name="wait">Wait until all jobs finished before returning?</param>
        /// <param name="numberOfProcessors">The maximum number of cores to use.</param>
        public void Run(IJobManager jobs, bool wait = false, int numberOfProcessors = -1)
        {
            // Determine number of threads to use
            if (numberOfProcessors == -1)
            {
                string numOfProcessorsString = Environment.GetEnvironmentVariable("NUMBER_OF_PROCESSORS");
                if (numOfProcessorsString != null)
                    numberOfProcessors = Convert.ToInt32(numOfProcessorsString);
                numberOfProcessors = System.Math.Max(numberOfProcessors, 1);
            }

            cancelToken = new CancellationTokenSource();

            // Run all jobs on background thread
            Task t = Task.Run(() => JobRunnerThread(jobs, numberOfProcessors));

            if (wait)
                while (!t.IsCompleted)
                    Thread.Sleep(200);
        }

        /// <summary>Stop all jobs currently running</summary>
        public virtual void Stop()
        {
            cancelToken.Cancel();
        }

        /// <summary>Main DoWork method for the scheduler thread. NB this does NOT run on the UI thread.        /// </summary>
        /// <param name="jobs">An instance of a class that manages all jobs.</param>
        /// <param name="numberOfTasksToUse">The number of tasks to run asyhchronously</param>
        private void JobRunnerThread(IJobManager jobs, int numberOfTasksToUse = -1)
        {
            Exception exceptionThrown = null;
            try
            {
                numberTasksRunning = 0;

                // Main worker thread for keeping jobs running
                while (!cancelToken.IsCancellationRequested)
                {
                    // Have we reached our maximum number of running jobs?
                    if (numberTasksRunning >= numberOfTasksToUse)
                        Thread.Sleep(200);  // Yes
                    else
                    {
                        // No - get another job to run.
                        IRunnable job = jobs.GetNextJobToRun();

                        // If no job available then exit - we're done.
                        if (job == null)
                            break;

                        // If the job is computationally intensive and will potentially take some time 
                        // to run, then we want to keep track of how many of these are running.
                        if (typeof(IComputationalyTimeConsuming).IsAssignableFrom(job.GetType()))
                        {
                            lock (this)
                                numberTasksRunning++;
                        }

                        // Run the job.
                        Task.Run(() =>
                        {
                            JobCompleteArgs jobCompleteArguments = new JobCompleteArgs();
                            jobCompleteArguments.job = job;
                            try
                            {
                                job.Run(cancelToken);
                            }
                            catch (Exception err)
                            {
                                jobCompleteArguments.exceptionThrowByJob = err;
                            }
                            if (JobCompleted != null)
                                JobCompleted.Invoke(this, jobCompleteArguments);

                            lock (this)
                                numberTasksRunning--;
                        });
                    }
                }
                // All jobs now completed
                while (numberTasksRunning > 0)
                    Thread.Sleep(200);

                jobs.Completed();
            }
            catch (Exception err)
            { 
                exceptionThrown = err;
            }

            if (AllJobsCompleted != null)
                AllJobsCompleted.Invoke(this, new AllCompletedArgs() { exceptionThrown = exceptionThrown });
        }
    }
}
