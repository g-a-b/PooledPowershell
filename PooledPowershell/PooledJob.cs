using System;
using System.Management.Automation;

namespace PooledPowershell
{
    /// <summary>
    /// Provides the event arguments passed to PooledJob object state change handlers.
    /// </summary>
    public class PooledJobInvocationStateChangedEventArgs
    {
        #region Members
        /// <summary>
        /// Contains information about the state of the PooledPowershell nested command pipeline.
        /// </summary>
        private PSInvocationStateInfo _stateInfo;
        #endregion

        #region Properties
        /// <summary>
        /// Gets information about the state of the PooledPowershell nested command pipeline.
        /// </summary>
        public PSInvocationStateInfo InvocationStateInfo
        {
            get { return _stateInfo; }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the PooledJobInvocationStateChangedEventArgs class based on the state provided.
        /// </summary>
        /// <param name="stateInfo">The state information to provide with the event.</param>
        internal PooledJobInvocationStateChangedEventArgs(PSInvocationStateInfo stateInfo)
        {
            _stateInfo = stateInfo;
        }
        #endregion
    }

    /// <summary>
    /// Provides a lightweight interface for the sealed PowerShell class. It can only be used within the classes inherited from PooledPowershell and cannot be invoked directly.
    /// It is based on the usage of a nested PowerShell class instance.
    /// </summary>
    public class PooledJob
    {
        #region Members
        /// <summary>
        /// A PowerShell instance that will perform the actual work.
        /// </summary>
        internal PowerShell _ps;

        /// <summary>
        /// Contains the handler returned by _ps.BeginInvoke. Null if the job is not running.
        /// </summary>
        internal IAsyncResult _executionHandler;

        /// <summary>
        /// Contains the output of the job.
        /// </summary>
        internal object _result;

        /// <summary>
        /// Contains the target of the job. Actually provided by the user for organization and ease purposes.
        /// </summary>
        private String _target;

        /// <summary>
        /// Contains a category for the job. Actually provided by the user for organization and ease purposes.
        /// </summary>
        private String _category;

        /// <summary>
        /// Contains a subcategory for the job. Actually provided by the user for organization and ease purposes.
        /// </summary>
        private String _subCategory;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the commands of the pipeline invoked by the PooledJob object.
        /// </summary>
        public PSCommand Commands
        {
            get { return _ps.Commands; }
            set { _ps.Commands = value; }
        }

        /// <summary>
        /// Gets the result(s) of the invocation of the pipeline.
        /// </summary>
        public object Result
        {
            get { return _result; }
        }

        /// <summary>
        /// Gets information about the current state of the invocation of the pipeline, such as whether it is running, completed, or failed.
        /// </summary>
        public PSInvocationStateInfo InvocationStateInfo
        {
            get { return _ps.InvocationStateInfo; }
        }

        /// <summary>
        /// Gets the global identifier for this instance of the PoooledJob object. Guaranteed to be unique.
        /// </summary>
        public Guid InstanceId
        {
            get { return _ps.InstanceId; }
        }

        /// <summary>
        /// Gets the data streams that contain any messages and error reports that were generated when the pipeline of the PooledJob object is invoked.
        /// </summary>
        public PSDataStreams Streams
        {
            get { return _ps.Streams; }
        }

        /// <summary>
        /// Gets the target of the job. Actually provided by the user when initializing the object for organization and ease purposes.
        /// </summary>
        public String Target
        {
            get { return _target; }
        }

        /// <summary>
        /// Contains a category for the job. Actually provided by the user when initializing the object for organization and ease purposes.
        /// </summary>
        public String Category
        {
            get { return _category; }
        }

        /// <summary>
        /// Contains a subcategory for the job. Actually provided by the user when initializing the object for organization and ease purposes.
        /// </summary>
        public String SubCategory
        {
            get { return _subCategory; }
        }
        #endregion

        #region Events
        /// <summary>
        /// Occurs when the state of the pipeline of the PooledJob object changes.
        /// </summary>
        public event EventHandler<PSInvocationStateChangedEventArgs> PooledJobInvocationStateChanged;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the PooledJob class based on a PowerShell object.
        /// </summary>
        /// <param name="ps">A PowerShell object. Should be part of a RunspacePool provided by the PooledPowershell class.</param>
        internal PooledJob(PowerShell ps)
        {
            _ps = ps;
            _ps.InvocationStateChanged += new EventHandler<PSInvocationStateChangedEventArgs>(_OnInvocationStateChanged);
        }

        /// <summary>
        /// Initializes a new instance of the PooledJob class based on a PowerShell object.
        /// </summary>
        /// <param name="ps">A PowerShell object. Should be part of a RunspacePool provided by the PooledPowershell class.</param>
        /// <param name="target">A String that represents the target of the job. For organization and ease purposes only.</param>
        internal PooledJob(PowerShell ps, String target)
            : this(ps)
        {
            _target = target;
        }

        /// <summary>
        /// Initializes a new instance of the PooledJob class based on a PowerShell object.
        /// </summary>
        /// <param name="ps">A PowerShell object. Should be part of a RunspacePool provided by the PooledPowershell class.</param>
        /// <param name="target">A String that represents the target of the job. For organization and ease purposes only.</param>
        /// <param name="category">A String that represents the category of the job. For organization and ease purposes only.</param>
        internal PooledJob(PowerShell ps, String target, String category)
            : this(ps, target)
        {
            _category = category;
        }

        /// <summary>
        /// Initializes a new instance of the PooledJob class based on a PowerShell object.
        /// </summary>
        /// <param name="ps">A PowerShell object. Should be part of a RunspacePool provided by the PooledPowershell class.</param>
        /// <param name="target">A String that represents the target of the job. For organization and ease purposes only.</param>
        /// <param name="category">A String that represents the category of the job. For organization and ease purposes only.</param>
        /// <param name="subCategory">A String that represents the subcategory of the job. For organization and ease purposes only.</param>
        internal PooledJob(PowerShell ps, String target, String category, String subCategory)
            : this(ps, target, category)
        {
            _subCategory = subCategory;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Raises a PooledJobInvocationStateChanged event when the nested Powershell instance raises an PSInvocationStateChanged event.
        /// </summary>
        /// <param name="sender">The nested PowerShell instance that raised the InvocationStateChanged event.</param>
        /// <param name="e">A PSInvocationStateChangedEventArgs object that represents the new state of the nested PowerShell object.</param>
        protected virtual void _OnInvocationStateChanged(object sender, PSInvocationStateChangedEventArgs e)
        {
            EventHandler<PSInvocationStateChangedEventArgs> handler = PooledJobInvocationStateChanged;
            if (handler != null)
                handler(this, e);
        }

        /// <summary>
        /// Adds an argument for a positional parameter of a command without specifying the parameter name.
        /// </summary>
        /// <param name="value">The value of the argument to be added to the last command of the pipeline.</param>
        /// <returns>A PooledJob object with the argument added at the end of the pipeline.</returns>
        public PooledJob AddArgument(Object value)
        {
            _ps.AddArgument(value);
            return this;
        }

        /// <summary>
        /// Adds a command to the end of the pipeline of the PooledJob object by specifying the command name.
        /// </summary>
        /// <param name="cmdlet">The name of the command to be added to the end of the pipeline. Do not include spaces immediately before or after the cmdlet name.</param>
        /// <returns>A PooledJob object whose pipeline includes the command at the end of the pipeline.</returns>
        public PooledJob AddCommand(String cmdlet)
        {
            _ps.AddCommand(cmdlet);
            return this;
        }

        /// <summary>
        /// Adds a cmdlet to the end of the pipeline of the PooledJob object, and specifies whether the cmdlet should be run within a new local scope.
        /// </summary>
        /// <param name="cmdlet">The name of the command to be added to the end of the pipeline. Do not include spaces immediately before or after the cmdlet name.</param>
        /// <param name="useLocalScope"><value>true</value> to run the cmdlet within a new local scope; otherwise, <value>false<v/alue>.</param>
        /// <returns>A PooledJob object whose pipeline includes the command at the end of the pipeline.</returns>
        public PooledJob AddCommand(String cmdlet, bool useLocalScope)
        {
            _ps.AddCommand(cmdlet, useLocalScope);
            return this;
        }

        /// <summary>
        /// Adds a switch parameter to the last command added to the pipeline.
        /// </summary>
        /// <param name="parameterName">The name of the switch parameter to be added.</param>
        /// <returns>A PooledJob object with the specified switch parameter added to the last command of the pipeline.</returns>
        public PooledJob AddParameter(String parameterName)
        {
            _ps.AddParameter(parameterName);
            return this;
        }

        /// <summary>
        /// Adds a parameter and value to the last command added to the pipeline.
        /// </summary>
        /// <param name="parameterName">The name of the parameter to be added.</param>
        /// <param name="value">The value of the parameter to be added.</param>
        /// <returns>A PooledJob object with the specified parameter added to the last command of the pipeline.</returns>
        public PooledJob AddParameter(String parameterName, object value)
        {
            if (value == null)
                _ps.AddParameter(parameterName);
            else
                _ps.AddParameter(parameterName, value);
            return this;
        }

        /// <summary>
        /// Adds a set of parameters to the last command of the pipeline. The parameter names and values are taken from the keys and values of the dictionary.
        /// </summary>
        /// <param name="parameters">A dictionary of the parameters to be added.</param>
        /// <returns>A PooledJob object with the specified parameters added to the last command of the pipeline.</returns>
        public PooledJob AddParameters(System.Collections.IDictionary parameters)
        {
            _ps.AddParameters(parameters);
            return this;
        }

        /// <summary>
        /// Adds a set of parameters to the last command of the pipeline. The parameter values are taken from the values in a list.
        /// </summary>
        /// <param name="parameters">A list of the parameters to be added.</param>
        /// <returns>A PooledJob object with the specified parameters added to the last command of the pipeline.</returns>
        public PooledJob AddParameters(System.Collections.IList parameters)
        {
            _ps.AddParameters(parameters);
            return this;
        }

        /// <summary>
        /// Adds a script to the end of the pipeline of the PooledJob object.
        /// </summary>
        /// <param name="script">The script to be added at the end of the pipeline. This parameter takes a piece of code that is treated as a script when the pipeline is invoked.</param>
        /// <returns>A PooledJob object that includes the script added to the end of the pipeline.</returns>
        public PooledJob AddScript(String script)
        {
            _ps.AddScript(script);
            return this;
        }

        /// <summary>
        /// Adds a script to the end of the pipeline of the PowerShell object, and indicates whether the script should be run in local scope.
        /// </summary>
        /// <param name="script">The script to be added at the end of the pipeline. This parameter takes a piece of code that is treated as a script when the pipeline is invoked.</param>
        /// <param name="useLocalScope">A Boolean value that indicates whether the script should be run in local scope. Be aware that when local scope is used, variables will be defined in a new scope so they will not be available when the script is complete.</param>
        /// <returns>A PooledJob object that includes the script at the end of the pipeline.</returns>
        public PooledJob AddScript(String script, bool useLocalScope)
        {
            _ps.AddScript(script, useLocalScope);
            return this;
        }
        #endregion
    }
}
