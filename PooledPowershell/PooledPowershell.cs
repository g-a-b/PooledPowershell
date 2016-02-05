using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Threading;
using Microsoft.Win32;

namespace PooledPowershell
{
    /// <summary>
    /// Defines the states of a PooledPowershell.
    /// </summary>
    public enum PooledPowershellInvocationState
    {
        /// <summary>
        /// The pool has finished to run.
        /// </summary>
        Completed,
        /// <summary>
        /// The pool is in a failed state.
        /// </summary>
        Failed, // Not used yet
        /// <summary>
        /// The pool has not yet been started. This is the initial state of a pool.
        /// </summary>
        NotStarted,
        /// <summary>
        /// The pool is running.
        /// </summary>
        Running,
        /// <summary>
        /// The pool has been stopped.
        /// </summary>
        Stopped, // Not used yet
        /// <summary>
        /// The pool has been required to stopped.
        /// </summary>
        Stopping // Not used yet
    }

    /// <summary>
    /// Defines methods to handle the state of a PooledPowershell run asynchronously
    /// </summary>
    internal sealed class PooledPowershellAsyncResult : IAsyncResult
    {
        #region Members
        /// <summary>
        /// An object used to lock the critical parts of the code.
        /// </summary>
        private object _syncObject = new object();

        /// <summary>
        /// The unique identifier of the associated pool. Basically the pool's Guid.
        /// </summary>
        private Guid _ownerId;

        /// <summary>
        /// Indicates whether the pool's jobs have completed.
        /// </summary>
        private bool _isCompleted;

        /// <summary>
        /// An object used to indicates the completion of the jobs.
        /// </summary>
        private ManualResetEvent _completedWaitHandle;

        /// <summary>
        /// The exception that may have occured during the invocation of the pool.
        /// </summary>
        private Exception _exception;

        /// <summary>
        /// A callback to be called after the completion of the jobs of the pool.
        /// </summary>
        private AsyncCallback _callback;

        /// <summary>
        /// The generic asynchronous state of the pool.
        /// </summary>
        private object _state;

        /// <summary>
        /// The amount of ended jobs. When this counter reaches the amount of total tasks, the PooledPowershell is marked as completed.
        /// </summary>
        internal uint _endedJobs = 0;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the generic asynchronous state of the pool.
        /// </summary>
        public object AsyncState
        {
            get { return _state; }
        }

        /// <summary>
        /// Gets the WaitHandle of the running pool.
        /// </summary>
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_completedWaitHandle == null)
                {
                    lock (_syncObject)
                    {
                        if (_completedWaitHandle == null)
                            _completedWaitHandle = new ManualResetEvent(_isCompleted);
                    }
                }
                return (WaitHandle)_completedWaitHandle;
            }
        }

        /// <summary>
        /// Gets a boolean that indicates whether the pool has completed asynchronously.
        /// </summary>
        public bool CompletedSynchronously
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a boolean that indicates whether the pool is completed.
        /// </summary>
        public bool IsCompleted
        {
            get { return _isCompleted; }
        }

        /// <summary>
        /// Gets the unique identifier of the associated pool. Basically the pool's Guid.
        /// </summary>
        internal Guid OwnerId
        {
            get
            {
                return _ownerId;
            }
        }

        /// <summary>
        /// Gets the exception that may have occured during the invocation of the pool.
        /// </summary>
        internal Exception Exception
        {
            get
            {
                return _exception;
            }
        }

        /// <summary>
        /// Gets the callback to be called after the completion of the jobs of the pool.
        /// </summary>
        internal AsyncCallback Callback
        {
            get
            {
                return _callback;
            }
        }

        /// <summary>
        /// Gets the object used to lock the critical parts of the code.
        /// </summary>
        internal object SyncObject
        {
            get
            {
                return _syncObject;
            }
        }
        #endregion

        #region Contructors
        /// <summary>
        /// Initializes a PooledPowershellAsyncResult based on the provided parameters.
        /// </summary>
        /// <param name="ownerId">The unique identifier of the associated pool. Basically the pool's Guid.</param>
        /// <param name="callback">A callback to be called after the completion of the jobs of the pool.</param>
        /// <param name="state">The generic asynchronous state of the pool.</param>
        internal PooledPowershellAsyncResult(Guid ownerId, AsyncCallback callback, object state)
        {
            if (ownerId == null)
                throw new ArgumentNullException("ownerId", "The unique identifier of the associated pool must be provided.");

            _ownerId = ownerId;
            _callback = callback;
            _state = state;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Indicates that all the jobs within the pool have completed.
        /// </summary>
        /// <param name="exception">An exception that may have occurred during the execution.</param>
        internal void SetAsCompleted(Exception exception)
        {
            if (_isCompleted)
                return;
            lock (_syncObject)
            {
                if (_isCompleted)
                    return;
                _exception = exception;
                _isCompleted = true;
                this.SignalWaitHandle();
            }
            if (_callback == null)
                return;
            _callback((IAsyncResult)this);
        }

        /// <summary>
        /// Indicates that all the jobs within the pool have completed but without calling the callback.
        /// </summary>
        internal void Release()
        {
            if (_isCompleted)
                return;
            _isCompleted = true;
            this.SignalWaitHandle();
        }

        /// <summary>
        /// Signals the waiting threads that all the jobs within the pool have completed.
        /// </summary>
        internal void SignalWaitHandle()
        {
            lock (_syncObject)
            {
                if (_completedWaitHandle == null)
                    return;
                _completedWaitHandle.Set();
            }
        }

        /// <summary>
        /// Cleans up the object.
        /// </summary>
        internal void EndInvoke()
        {
            this.AsyncWaitHandle.WaitOne();
            this.AsyncWaitHandle.Close();
            _completedWaitHandle = (ManualResetEvent)null;
            if (_exception != null)
                throw _exception;
        }
        #endregion
    }

    /// <summary>
    /// Provides information about the current state of the invocation process of the pool, such as what the current state is and, if a failure occurred, the reason for the last state change.
    /// </summary>
    public sealed class PooledPowershellInvocationStateInfo
    {
        #region Members
        /// <summary>
        /// The reason for the last state change if the state changed because of an error.
        /// </summary>
        private Exception _reason;

        /// <summary>
        /// The current state of the invocation of the pool.
        /// </summary>
        private PooledPowershellInvocationState _state;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the reason for the last state change if the state changed because of an error.
        /// </summary>
        public Exception Reason
        {
            get { return _reason; }
        }

        /// <summary>
        /// Gets the current state of the invocation of the pool.
        /// </summary>
        public PooledPowershellInvocationState State
        {
            get { return _state; }
        }
        #endregion

        #region Contructors
        /// <summary>
        /// Initializes a new <c>PooledPowershellInvocationStateInfo</c> object setting the sate to <c>NotStarted</c>.
        /// <seealso cref="PooledPowershellInvocationStateInfo._state"/>
        /// </summary>
        internal PooledPowershellInvocationStateInfo()
        {
            _state = PooledPowershellInvocationState.NotStarted;
            _reason = null;
        }

        internal PooledPowershellInvocationStateInfo(PooledPowershellInvocationState state)
        {
            _state = state;
            _reason = null;
        }

        internal PooledPowershellInvocationStateInfo(PooledPowershellInvocationState state, Exception ex)
        {
            _state = PooledPowershellInvocationState.NotStarted;
            _reason = ex;
        }

        internal PooledPowershellInvocationStateInfo Clone()
        {
            return new PooledPowershellInvocationStateInfo(_state, _reason);
        }
        #endregion
    }

    /// <summary>
    /// The exception thrown when an operation that is not valid is requested.
    /// </summary>
    public class PooledPowershellInvalidOperationException : PSInvalidOperationException
    {
        #region Constructors
        /// <summary>
        /// Creates a new <c>PooledPowershellInvalidOperationException</c> object that is empty. 
        /// </summary>
        public PooledPowershellInvalidOperationException() : base("The operation is not permitted when the object is in this state.") { }

        /// <summary>
        /// Creates a new <c>PooledPowershellInvalidOperationException</c> object that contains an exception message.
        /// </summary>
        /// <param name="message">The exception message that describes the error condition.</param>
        public PooledPowershellInvalidOperationException(string message) : base(message) { }

        /// <summary>
        /// Creates a new <c>PooledPowershellInvalidOperationException</c> object that contains an exception message and the inner-exception that caused this exception to be thrown.
        /// </summary>
        /// <param name="message">The exception message that describes the error condition.</param>
        /// <param name="innerException">The specific exception that caused the <c>PooledPowershellInvalidOperationException</c> exception to be thrown.</param>
        public PooledPowershellInvalidOperationException(string message, Exception innerException) : base(message, innerException) { }
        #endregion
    }

    /// <summary>
    /// The exception thrown when an operation is requested whereas the pool has already been disposed.
    /// </summary>
    public class PooledPowershellObjectDisposedException : PSObjectDisposedException
    {
        #region Constructors
        /// <summary>
        /// Creates a new <c>PooledPowershellObjectDisposedException</c> object that is empty. 
        /// </summary>
        public PooledPowershellObjectDisposedException() : base("The pool has been disposed and can no longer be used.") { }

        /// <summary>
        /// Creates a new <c>PooledPowershellObjectDisposedException</c> object that contains an exception message.
        /// </summary>
        /// <param name="message">The exception message that describes the error condition.</param>
        public PooledPowershellObjectDisposedException(string message) : base(message) { }

        /// <summary>
        /// Creates a new <c>PooledPowershellObjectDisposedException</c> object that contains an exception message and the inner-exception that caused this exception to be thrown.
        /// </summary>
        /// <param name="message">The exception message that describes the error condition.</param>
        /// <param name="innerException">The specific exception that caused the <c>PooledPowershellObjectDisposedException</c> exception to be thrown.</param>
        public PooledPowershellObjectDisposedException(string message, Exception innerException) : base(message, innerException) { }
        #endregion
    }

    /// <summary>
    /// Provides a generic execution environment for <c>PooledJob</c>s and basically consists in a PowerShell <c>RunspacePool</c> the PooledJobs are attached to.
    /// It allows the jobs to be run either synchronously or asynchronously and provides ways to follow the execution of the batch.
    /// This class must be inherited.
    /// </summary>
    public abstract class PooledPowershell : IDisposable
    {
        #region Members
        /// <summary>
        /// An object used to lock the critical parts of the code.
        /// </summary>
        private object _syncObject = new object();

        /// <summary>
        /// The PowerShell RunspacePool the powershell jobs are going to run in.
        /// </summary>
        protected RunspacePool _rsPool;

        /// <summary>
        /// The minimal number of runspace available in the runspace pool.
        /// </summary>
        protected int _minRunspaces;

        /// <summary>
        /// The maximal number of runspace available in the runspace pool.
        /// </summary>
        protected int _maxRunspaces;

        /// <summary>
        /// A dictionnary that contains the <c>PooledJob</c>s associated with the pool indexed by their Guid.
        /// </summary>
        private Dictionary<Guid, PooledJob> _jobs;

        /// <summary>
        /// A unique identifier for the pool.
        /// </summary>
        private Guid _poolGuid = new Guid();

        /// <summary>
        /// A boolean indicating whether the pool has been disposed.
        /// </summary>
        private bool _isDisposed = false;

        /// <summary>
        /// An object used to follow the state of the pool when run asynchronously.
        /// </summary>
        private PooledPowershellAsyncResult _invokeAsyncResult;

        /// <summary>
        /// An object used to follow the state of the pool when stopped asynchronously.
        /// </summary>
        private PooledPowershellAsyncResult _stopAsyncResult;

        /// <summary>
        /// The current state of the invocation process of the pool.
        /// </summary>
        private PooledPowershellInvocationStateInfo _invocationStateInfo = new PooledPowershellInvocationStateInfo();
        #endregion

        #region Properties
        /// <summary>
        /// Gets the current state of the invocation process of the pool.
        /// </summary>
        public PooledPowershellInvocationStateInfo InvocationStateInfo
        {
            get { return _invocationStateInfo; }
        }

        /// <summary>
        /// Gets the list of the <c>PooledJob</c>s associated with the pool.
        /// </summary>
        public PooledJob[] Jobs
        {
            get
            {
                PooledJob[] jobs = new PooledJob[_jobs.Count];
                _jobs.Values.CopyTo(jobs, 0);
                return jobs;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new pool based on the parameters provided.
        /// </summary>
        /// <param name="minRunspaces">The minimal number of runspace available in the underlying runspace pool. Must be greater than 0.</param>
        /// <param name="maxRunspaces">The maximal number of runspace available in the underlying runspace pool. Must be at least equal to <paramref name="minRunspaces"/>.</param>
        protected PooledPowershell(int minRunspaces, int maxRunspaces)
        {
            if (minRunspaces < 1)
                throw new ArgumentOutOfRangeException("minRunspaces", "The parameter minRunspaces must be greater or equal to 1.");

            if (maxRunspaces < 1)
                throw new ArgumentOutOfRangeException("maxRunspaces", "The parameter maxRunspaces must be greater or equal to 1.");

            if (minRunspaces > maxRunspaces)
                throw new ArgumentException("The parameter maxRunspaces must be greater or equal to minRunspaces.");

            _minRunspaces = minRunspaces;
            _maxRunspaces = maxRunspaces;
            _jobs = new Dictionary<Guid, PooledJob>();
        }
        #endregion

        #region Methods
        /// <summary>
        /// Initializes a <c>PooledJob</c> associated to the pool.
        /// </summary>
        /// <returns>A PooledJob that cannot be disassociated from the pool.</returns>
        public PooledJob CreatePooledJob()
        {
            PowerShell ps = _GetPipeline();
            PooledJob pj = new PooledJob(ps);
            _jobs.Add(pj.InstanceId, pj);
            return pj;
        }

        /// <summary>
        /// Initializes a <c>PooledJob</c> associated to the pool specifying a user-provided target.
        /// </summary>
        /// <param name="target">A <c>String</c> provided by the user that represents the target of the job.</param>
        /// <returns>A PooledJob that cannot be disassociated from the pool.</returns>
        public PooledJob CreatePooledJob(String target)
        {
            if (String.IsNullOrEmpty(target))
                throw new ArgumentNullException("target", "The target cannot be null or empty.");

            PowerShell ps = _GetPipeline();
            PooledJob pj = new PooledJob(ps, target);
            _jobs.Add(pj.InstanceId, pj);
            return pj;
        }

        /// <summary>
        /// Initializes a <c>PooledJob</c> associated to the pool specifying a user-provided target and category.
        /// </summary>
        /// <param name="target">A <c>String</c> provided by the user that represents the target of the job.</param>
        /// <param name="category">A <c>String</c> provided by the user that represents the category of the job.</param>
        /// <returns>A PooledJob that cannot be disassociated from the pool.</returns>
        public PooledJob CreatePooledJob(String target, String category)
        {
            if (String.IsNullOrEmpty(target))
                throw new ArgumentNullException("target", "The target cannot be null or empty.");

            if (String.IsNullOrEmpty(category))
                throw new ArgumentNullException("category", "The category cannot be null or empty.");

            PowerShell ps = _GetPipeline();
            PooledJob pj = new PooledJob(ps, target, category);
            _jobs.Add(pj.InstanceId, pj);
            return pj;
        }

        /// <summary>
        /// Initializes a <c>PooledJob</c> associated to the pool specifying a user-provided target, category and sub-category.
        /// </summary>
        /// <param name="target">A <c>String</c> provided by the user that represents the target of the job.</param>
        /// <param name="category">A <c>String</c> provided by the user that represents the category of the job.</param>
        /// <param name="subCategory">A <c>String</c> provided by the user that represents the sub-category of the job.</param>
        /// <returns>A PooledJob that cannot be disassociated from the pool.</returns>
        public PooledJob CreatePooledJob(String target, String category, String subCategory)
        {
            if (String.IsNullOrEmpty(target))
                throw new ArgumentNullException("target", "The target cannot be null or empty.");

            if (String.IsNullOrEmpty(category))
                throw new ArgumentNullException("category", "The category cannot be null or empty.");

            if (String.IsNullOrEmpty(subCategory))
                throw new ArgumentNullException("subCategory", "The subCategory cannot be null or empty.");

            PowerShell ps = _GetPipeline();
            PooledJob pj = new PooledJob(ps, target, category, subCategory);
            _jobs.Add(pj.InstanceId, pj);
            return pj;
        }

        /// <summary>
        /// Gets an empty pipeline of <c>PowerShell</c> commands, associated with the <c>RunspacePool</c> used by the pool.
        /// </summary>
        /// <returns>A PowerShell object with an empty pipeline.</returns>
        private PowerShell _GetPipeline()
        {
            if (_isDisposed)
                throw new PooledPowershellObjectDisposedException();

            PowerShell ps = PowerShell.Create();
            ps.RunspacePool = _rsPool;
            return ps;
        }

        /// <summary>
        /// Releases all resources used by the PooledPowershell object. Does not release the resources used by the underlying <c>PooledJob</c>s.
        /// </summary>
        public void Dispose()
        {
            lock (_syncObject)
            {
                if (_isDisposed)
                    return;

                if (_invocationStateInfo.State == PooledPowershellInvocationState.Running || _invocationStateInfo.State == PooledPowershellInvocationState.Stopping)
                    throw new PooledPowershellInvalidOperationException("A pool cannot be disposed while it is running or stopping.");

                _isDisposed = true;
            }

            _rsPool.Close();
            _rsPool.Dispose();
        }

        /// <summary>
        /// Removes all the jobs from the pool.
        /// </summary>
        /// <remarks>When calling this method, the underlying PowerShell pipelines will be disposed.</remarks>
        public void Purge()
        {
            lock (_syncObject)
            {
                if (_invocationStateInfo.State == PooledPowershellInvocationState.Running || _invocationStateInfo.State == PooledPowershellInvocationState.Stopping)
                    throw new PooledPowershellInvalidOperationException("The pool is running. You must stop the pool prior to purge it.");

                _invocationStateInfo = new PooledPowershellInvocationStateInfo();


                foreach (PooledJob job in _jobs.Values)
                {
                    job._ps.RunspacePool = null;
                    job._ps.Dispose();
                }

                _jobs.Clear();
            }
        }

        /// <summary>
        /// Synchronously runs the commands of the <c>PooledJob</c> objects pipeline.
        /// </summary>
        public void Invoke()
        {
            BeginInvoke();
            EndInvoke();
        }

        /// <summary>
        /// Asynchronously runs the commands of the <c>PooledJob</c> objects pipeline.
        /// </summary>
        /// <returns>An <c>IAsyncResult</c> interface that represents the status of the asynchronous operation. The <see cref="EndInvoke"/> method uses this interface to determine when to return the results of the operation.</returns>
        public IAsyncResult BeginInvoke()
        {
            return BeginInvoke(null, null);
        }

        /// <summary>
        /// Asynchronously runs the commands of the <c>PooledJob</c> objects pipeline by using a specified callback method to use when the invocation is complete.
        /// </summary>
        /// <param name="callback">An AsyncCallback delegate that is called when the command invoked by the method is complete.</param>
        /// <param name="state">A user-supplied state to use when the callback method is called.</param>
        /// <returns>An <c>IAsyncResult</c> interface that represents the status of the asynchronous operation. The <see cref="EndInvoke"/> method uses this interface to determine when to return the results of the operation.</returns>
        public IAsyncResult BeginInvoke(AsyncCallback callback, object state)
        {
            lock (_syncObject)
            {
                if (_isDisposed)
                    throw new PooledPowershellObjectDisposedException();

                if (_invocationStateInfo.State == PooledPowershellInvocationState.Running || _invocationStateInfo.State == PooledPowershellInvocationState.Stopping)
                    throw new PooledPowershellInvalidOperationException("The pool is already running.");

                _invokeAsyncResult = new PooledPowershellAsyncResult(_poolGuid, callback, state);
                _invocationStateInfo = new PooledPowershellInvocationStateInfo(PooledPowershellInvocationState.Running);


                foreach (PooledJob job in _jobs.Values)
                    job._executionHandler = job._ps.BeginInvoke((PSDataCollection<PSObject>)null, (PSInvocationSettings)null, _PSCallback, job.InstanceId);
            }

            return (IAsyncResult)_invokeAsyncResult;
        }

        /// <summary>
        /// Method called by the object after each <c>PooledJob</c> has completed.
        /// </summary>
        /// <param name="asyncResult">The result of the asynchronous operation. Must actually contain the <c>PooledJob</c>'s Guid.</param>
        private void _PSCallback(IAsyncResult asyncResult)
        {
            lock (_syncObject)
            {
                PooledJob job = _jobs[(Guid)asyncResult.AsyncState];

                if (asyncResult.IsCompleted)
                {
                    try
                    {
                        job._result = job._ps.EndInvoke(job._executionHandler);
                    }
                    catch
                    {
                        /* Nothing to do. */
                    }
                    finally
                    {
                        job._executionHandler = null;
                        asyncResult.AsyncWaitHandle.Close();
                        _invokeAsyncResult._endedJobs++;

                        if (_invokeAsyncResult._endedJobs == _jobs.Count)
                        {
                            _invokeAsyncResult.SetAsCompleted(null);
                            _invocationStateInfo = new PooledPowershellInvocationStateInfo(PooledPowershellInvocationState.Completed);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Waits indefinitely for the pending asynchronous BeginInvoke call to be completed.
        /// </summary>
        /// <returns><value>true</value> if the pool receives a completion signal. If the current instance is never signaled, EndInvoke never returns.</returns>
        public bool EndInvoke()
        {
            return EndInvoke(Timeout.Infinite);
        }

        /// <summary>
        /// Waits temporarily for the pending asynchronous BeginInvoke call to be completed.
        /// </summary>
        /// <param name="timeout">A TimeSpan that represents the number of milliseconds to wait. Beyond this limit, the <c>PooledJob</c>s that are still running are forcibly stopped.</param>
        /// <returns><value>true</value> if the pool receives a completion signal; otherwise, <value>false</value>.</returns>
        public bool EndInvoke(TimeSpan timeout)
        {
            return EndInvoke((int)timeout.TotalMilliseconds);
        }

        /// <summary>
        /// Waits temporarily for the pending asynchronous BeginInvoke call to be completed.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see cref="Timeout.Infinite"/> (-1) to wait indefinitely. Beyond this limit, the <c>PooledJob</c>s that are still running are forcibly stopped.</param>
        /// <returns><value>true</value> if the pool receives a completion signal; otherwise, <value>false</value>.</returns>
        public bool EndInvoke(int millisecondsTimeout)
        {
            // The EndInvoke method should not be called if the pool is not either running or completed
            lock (_syncObject)
            {
                if (_invocationStateInfo.State != PooledPowershellInvocationState.Running && _invocationStateInfo.State != PooledPowershellInvocationState.Completed)
                    throw new PooledPowershellInvalidOperationException("The pool is either not running or not completed.");
            }

            bool signalReceived = _invokeAsyncResult.AsyncWaitHandle.WaitOne(millisecondsTimeout);
            _invokeAsyncResult.AsyncWaitHandle.Close();

            List<WaitHandle> stoppingJobsWaitHandles = new List<WaitHandle>();

            lock (_syncObject)
            {
                // Let's check if all the jobs are completed and if some aren't, let's stop them
                foreach (PooledJob job in _jobs.Values)
                {
                    // _executionHandler is set to null after job's completion (whatever it sucessed or failed)
                    // So if it's not null, the job is still running and must be stopped
                    if (job._executionHandler != null)
                    {
                        stoppingJobsWaitHandles.Add(job._ps.BeginStop(null, null).AsyncWaitHandle);
                        job._executionHandler = null;
                    }
                }
            }

            // Waiting for all the stop operations to complete
            if (stoppingJobsWaitHandles.Count > 0)
                WaitHandle.WaitAll(stoppingJobsWaitHandles.ToArray());

            lock (_syncObject)
            {
                _invocationStateInfo = new PooledPowershellInvocationStateInfo(PooledPowershellInvocationState.Completed);
            }

            return signalReceived;
        }

        public IAsyncResult BeginStop()
        {
            return BeginStop(null, null);
        }

        public IAsyncResult BeginStop(AsyncCallback callback, object state)
        {
            throw new NotImplementedException();
        }
        #endregion
    }

    /// <summary>
    /// Provides an execution environment for <c>PooledJob</c>s based on the built-in PowerShell CommandLets.
    /// </summary>
    public class BasicPooledPowershell : PooledPowershell
    {
        #region Constructors
        /// <summary>
        /// Initializes a new Basic pool based on the parameters provided.
        /// </summary>
        /// <param name="minRunspaces">The minimal number of runspace available in the underlying runspace pool. Must be greater than 0.</param>
        /// <param name="maxRunspaces">The maximal number of runspace available in the underlying runspace pool. Must be at least equal to <paramref name="minRunspaces"/>.</param>
        /// <param name="iss">A configuration object used to define many options about the underlying runspace pool.</param>
        public BasicPooledPowershell(int minRunspaces, int maxRunspaces, InitialSessionState iss = null)
            : base(minRunspaces, maxRunspaces)
        {
            try
            {

                if (iss != null)
                {
                    _rsPool = RunspaceFactory.CreateRunspacePool(iss);
                    _rsPool.SetMaxRunspaces(_maxRunspaces);
                    _rsPool.SetMinRunspaces(_minRunspaces);
                }
                else
                {
                    _rsPool = RunspaceFactory.CreateRunspacePool(minRunspaces, maxRunspaces);
                }
                
                _rsPool.Open();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion
    }

    /// <summary>
    /// Provides an execution environment for <c>PooledJob</c>s based on the Exchange 2007 PowerShell CommandLets.
    /// The Exchange 2007 Management Tools must be installed on the host.
    /// </summary>
    public class Ex2007PooledPowershell : PooledPowershell
    {
        #region Constructors
        /// <summary>
        /// Initializes a new Exchange 2007 pool based on the parameters provided.
        /// </summary>
        /// <param name="minRunspaces">The minimal number of runspace available in the underlying runspace pool. Must be greater than 0.</param>
        /// <param name="maxRunspaces">The maximal number of runspace available in the underlying runspace pool. Must be at least equal to <paramref name="minRunspaces"/>.</param>
        /// <param name="psHost">A <see cref="System.Management.Automation.Host.PSHost"/> object that represents the host that provides communications between Windows PowerShell and the user. From a PowerShell script, one can use $Host.</param>
        /// <param name="iss">A configuration object used to define many options about the underlying runspace pool.</param>
        public Ex2007PooledPowershell(int minRunspaces, int maxRunspaces, PSHost psHost, InitialSessionState iss = null)
            : base(minRunspaces, maxRunspaces)
        {
            if (psHost == null)
                throw new ArgumentNullException("psHost", "The PSHost cannot be null.");

            try
            {
                if (iss == null)
                    iss = InitialSessionState.CreateDefault();

                PSSnapInException psEx;
                iss.ImportPSSnapIn("Microsoft.Exchange.Management.PowerShell.Admin", out psEx);
                _rsPool = RunspaceFactory.CreateRunspacePool(minRunspaces, maxRunspaces, iss, psHost);
                _rsPool.Open();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion
    }

    /// <summary>
    /// Provides an execution environment for <c>PooledJob</c>s based on the Exchange 2010 PowerShell CommandLets.
    /// It connects remotely using a WSMan connection to a host that must have the Exchange 2010 Management Tools installed in.
    /// </summary>
    public class Ex2010PooledPowershell : PooledPowershell
    {
        #region Members
        /// <summary>
        /// The WSMan connection used to connect to the remote host.
        /// </summary>
        private WSManConnectionInfo _wsManConnInfo;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new Exchange 2010 pool based on the parameters provided.
        /// </summary>
        /// <param name="minRunspaces">The minimal number of runspace available in the underlying runspace pool. Must be greater than 0.</param>
        /// <param name="maxRunspaces">The maximal number of runspace available in the underlying runspace pool. Must be at least equal to <paramref name="minRunspaces"/>.</param>
        /// <param name="targetServerFqdn">The FQDN of the remote server to connect to.</param>
        /// <param name="iss">A configuration object used to define many options about the underlying runspace pool.</param>
        public Ex2010PooledPowershell(int minRunspaces, int maxRunspaces, String targetServerFqdn)
            : base(minRunspaces, maxRunspaces)
        {
            if (String.IsNullOrEmpty(targetServerFqdn))
                throw new ArgumentNullException("targetServerFqdn", "The server's fqdn cannot be null or empty.");

            String connectionString;

            RegistryKey setupRegistryEntry = Registry.LocalMachine.OpenSubKey("Software\\Microsoft\\ExchangeServer\\v14\\Setup");
            if (setupRegistryEntry != null)
            {
                String clientVersion = String.Format("{0}.{1}.{2}.{3}",
                    setupRegistryEntry.GetValue("MsiProductMajor"),
                    setupRegistryEntry.GetValue("MsiProductMinor"),
                    setupRegistryEntry.GetValue("MsiBuildMajor"),
                    setupRegistryEntry.GetValue("MsiBuildMinor"));
                connectionString = String.Format("http://{0}/powershell?serializationLevel=Full;ExchClientVer={1}", targetServerFqdn, clientVersion);
            }
            else
            {
                connectionString = String.Format("http://{0}/powershell?serializationLevel=Full", targetServerFqdn);
            }

            try
            {
                Uri connectionUri = new Uri(connectionString);
                _wsManConnInfo = new WSManConnectionInfo(connectionUri, "http://schemas.microsoft.com/powershell/Microsoft.Exchange", (PSCredential)null);
                _rsPool = RunspaceFactory.CreateRunspacePool(minRunspaces, maxRunspaces, _wsManConnInfo);
                _rsPool.Open();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion
    }
}
