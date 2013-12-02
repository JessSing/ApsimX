﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

using Models.Core;

namespace Models.PMF.Functions
{
    /// <summary>
    /// Returns the value of a nominated external APSIM numerical variable.
    /// Note: This should be merged with the variable function when naming convention
    /// to refer to internal and external variable is standardized. FIXME
    /// </summary>
    [Description("Returns the value of a nominated external APSIM numerical variable")]
    public class ExternalVariable : Function
    {
        public string VariableName = "";


        
        public override double Value
        {
            get
            {
                object val = this.Get(VariableName);

                if (val != null)
                    return Convert.ToDouble(val);
                else
                    throw new Exception(Name + ": External value for " + VariableName.Trim() + " not found");
            }
        }

    }
}