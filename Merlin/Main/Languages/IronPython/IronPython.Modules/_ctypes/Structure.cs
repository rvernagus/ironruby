/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

#if !SILVERLIGHT
namespace IronPython.Modules {
    /// <summary>
    /// Provides support for interop with native code from Python code.
    /// </summary>
    public static partial class CTypes {

        /// <summary>
        /// Base class for data structures.  Subclasses can define _fields_ which 
        /// specifies the in memory layout of the values.  Instances can then
        /// be created with the initial values provided as the array.  The values
        /// can then be accessed from the instance by field name.  The value can also
        /// be passed to a foreign C API and the type can be used in other structures.
        /// 
        /// class MyStructure(Structure):
        ///     _fields_ = [('a', c_int), ('b', c_int)]
        ///     
        /// MyStructure(1, 2).a
        /// MyStructure()
        /// 
        /// class MyOtherStructure(Structure):
        ///     _fields_ = [('c', MyStructure), ('b', c_int)]
        ///     
        /// MyOtherStructure((1, 2), 3)
        /// MyOtherStructure(MyStructure(1, 2), 3)
        /// </summary>
        [PythonType("Structure")]
        public abstract class _Structure : CData {
            protected _Structure() {
                ((StructType)NativeType).EnsureFinal();
                _memHolder = new MemoryHolder(NativeType.Size);
            }

            public void __init__(params object[] args) {
                CheckAbstract();

                INativeType nativeType = NativeType;
                StructType st = (StructType)nativeType;

                st.SetValueInternal(_memHolder, 0, args);
            }

            public void __init__(CodeContext/*!*/ context, [ParamDictionary]IAttributesCollection kwargs) {
                CheckAbstract();

                INativeType nativeType = NativeType;

                StructType st = (StructType)nativeType;

                foreach (var x in kwargs.SymbolAttributes) {
                    PythonOps.SetAttr(context, this, x.Key, x.Value);
                }
            }

            private void CheckAbstract() {
                object abstractCls;
                if (((PythonType)NativeType).TryGetBoundAttr(((PythonType)NativeType).Context.SharedContext,
                    this,
                    SymbolTable.StringToId("_abstract_"),
                    out abstractCls)) {
                    throw PythonOps.TypeError("abstract class");
                }
            }
        }
    }

}
#endif
