/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * ironruby@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using Microsoft.Scripting;
using Microsoft.Scripting.Actions;
using Microsoft.Scripting.Utils;
using IronRuby.Compiler.Ast;
using IronRuby.Runtime;

namespace IronRuby.Compiler {
    using MSA = System.Linq.Expressions;
    using Ast = System.Linq.Expressions.Expression;

    public abstract class LexicalScope : HybridStringDictionary<LocalVariable> {
        // Null if there is no parent lexical scopes whose local variables are visible eto this scope.
        // Scopes:
        // - method and module: null
        // - source unit: RuntimeLexicalScope if eval, null otherwise
        // - block: non-null
        private readonly LexicalScope _outerScope;

        //
        // Lexical depth relative to the inner-most scope that doesn't inherit locals from its parent.
        // If depth >= 0 the scope defines static variables, otherwise it defines dynamic variables. 
        // 
        private readonly int _depth;

        // Note on dynamic scopes. 
        // We don't statically define variables defined in top-level eval'd code so the depth of the top-level scope is -1 
        // if the outer scope is a runtime scope.
        //
        // eval('x = 1')   <-- this variable needs to be defined in containing runtime scope, not in top-level eval scope
        // eval('puts x')
        // 
        // eval('1.times { x = 1 }')  <-- x could be statically defined in the block since it is not visible outside the block
        //
        internal LexicalScope(LexicalScope outerScope)
            : this(outerScope, (outerScope != null) ? (outerScope.IsRuntimeScope ? -1 : outerScope._depth + 1) : 0) {
        }

        protected LexicalScope(LexicalScope outerScope, int depth) {
            _outerScope = outerScope;
            _depth = depth;
        }

        public int Depth {
            get { return _depth; }
        }

        public LexicalScope OuterScope {
            get { return _outerScope; }
        }

        protected virtual bool IsRuntimeScope {
            get { return false; }
        }

        protected virtual bool IsTop {
            get { return false; }
        }

        protected virtual bool AllowsVariableDefinitions {
            get { return true; }
        }

        public LocalVariable/*!*/ AddVariable(string/*!*/ name, SourceSpan location) {
            Debug.Assert(AllowsVariableDefinitions);
            var var = new LocalVariable(name, location, _depth);
            Add(name, var);
            return var;
        }

        public LocalVariable/*!*/ ResolveOrAddVariable(string/*!*/ name, SourceSpan location) {
            var result = ResolveVariable(name);
            
            if (result != null) {
                return result;
            }

            var targetScope = this;
            while (!targetScope.AllowsVariableDefinitions) {
                targetScope = targetScope.OuterScope;
            }

            return targetScope.AddVariable(name, location);
        }

        public LocalVariable ResolveVariable(string/*!*/ name) {
            LexicalScope scope = this;
            do {
                LocalVariable result;
                if (scope.TryGetValue(name, out result)) {
                    return result;
                }
                scope = scope.OuterScope;
            } while (scope != null);
            return null;
        }

        internal LexicalScope/*!*/ GetInnerMostTopScope() {
            Debug.Assert(!IsRuntimeScope);

            LexicalScope scope = this;
            while (!scope.IsTop) {
                scope = scope.OuterScope;
                Debug.Assert(scope != null);
            }
            return scope;
        }

        #region Transformation

        internal int AllocateClosureSlotsForLocals(int closureIndex) {
            int localCount = 0;
            foreach (var local in this) {
                if (local.Value.ClosureIndex == -1) {
                    local.Value.SetClosureIndex(closureIndex++);
                    localCount++;
                }
            }
            return localCount;
        }

        internal static void TransformParametersToSuperCall(AstGenerator/*!*/ gen, CallBuilder/*!*/ callBuilder, Parameters parameters) {
            if (parameters == null) {
                return;
            }

            if (parameters.Mandatory != null) {
                foreach (Variable v in parameters.Mandatory) {
                    callBuilder.Add(v.TransformRead(gen));
                }
            }

            if (parameters.Optional != null) {
                foreach (SimpleAssignmentExpression s in parameters.Optional) {
                    callBuilder.Add(s.Left.TransformRead(gen));
                }
            }

            if (parameters.Array != null) {
                callBuilder.SplattedArgument = parameters.Array.TransformRead(gen);
            }
        }

        #endregion
    }

    /// <summary>
    /// Method, module and source unit scopes.
    /// </summary>
    internal sealed class TopLexicalScope : LexicalScope {
        public TopLexicalScope(LexicalScope outerScope)
            : base(outerScope) {
        }

        protected override bool IsTop {
            get { return true; }
        }
    }

    /// <summary>
    /// Block scope.
    /// </summary>
    internal sealed class BlockLexicalScope : LexicalScope {
        public BlockLexicalScope(LexicalScope outerScope)
            : base(outerScope) {
        }
    }

    /// <summary>
    /// for-loop scope.
    /// </summary>
    internal sealed class PaddingLexicalScope : LexicalScope {
        public PaddingLexicalScope(LexicalScope outerScope) 
            : base(outerScope) {
            Debug.Assert(outerScope != null);
        }

        protected override bool AllowsVariableDefinitions {
            get { return false; }
        }
    }

    // Scope contains variables defined outside of the current compilation unit. Used for assertion checks only.
    // (e.g. created for variables in the runtime scope of eval).
    internal sealed class RuntimeLexicalScope : LexicalScope {
        public RuntimeLexicalScope(List<string>/*!*/ names)
            : base(null, -1) {

            for (int i = 0; i < names.Count; i++) {
                AddVariable(names[i], SourceSpan.None);
            }
        }

        protected override bool IsRuntimeScope {
            get { return true; }
        }
    }

    #region HybridStringDictionary

    public class HybridStringDictionary<TValue> : IEnumerable<KeyValuePair<string, TValue>> {
        // Number of variables in scopes during Rails startup:
        // #variables    0     1     2    3    4    5   6+
        // #scopes    4587  3814  1994  794  608  220  295
        private const int ListLength = 4;

        private Dictionary<string, TValue> _dict;
        private KeyValuePair<string, TValue>[] _list;
        private int _listSize;

        public int Count {
            get { return _listSize + (_dict != null ? _dict.Count : 0); }
        }

        public bool TryGetValue(string key, out TValue value) {
            for (int i = 0; i < _listSize; i++) {
                var entry = _list[i];
                if (entry.Key == key) {
                    value = entry.Value;
                    return true;
                }
            }

            if (_dict != null) {
                return _dict.TryGetValue(key, out value);
            }

            value = default(TValue);
            return false;
        }

        public void Add(string key, TValue value) {
            if (_listSize > 0) {
                if (_listSize < _list.Length) {
                    _list[_listSize++] = new KeyValuePair<string, TValue>(key, value);
                } else {
                    _dict = new Dictionary<string, TValue>();
                    for (int i = 0; i < _list.Length; i++) {
                        var entry = _list[i];
                        _dict.Add(entry.Key, entry.Value);
                    }
                    _dict.Add(key, value);
                    _list = null;
                    _listSize = -1;
                }
            } else if (_listSize == 0) {
                Debug.Assert(_list == null);
                _list = new KeyValuePair<string, TValue>[ListLength];
                _list[0] = new KeyValuePair<string, TValue>(key, value);
                _listSize = 1;
            } else {
                Debug.Assert(_listSize == -1 && _dict != null);
                _dict.Add(key, value);
            }
        }

        IEnumerator<KeyValuePair<string, TValue>>/*!*/ IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator() {
            for (int i = 0; i < _listSize; i++) {
                yield return _list[i];
            }

            if (_dict != null) {
                foreach (var entry in _dict) {
                    yield return entry;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return ((IEnumerable<KeyValuePair<string, TValue>>)this).GetEnumerator();
        }
    }

    #endregion
}

