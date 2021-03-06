﻿/* ****************************************************************************
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
using System.Text;
using System.Threading;
using Microsoft.Scripting.Utils;
using IronRuby.Builtins;
using IronRuby.Runtime;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting;

namespace IronRuby.Builtins {
    [RubyClass("Name", Extends = typeof(ClrName), DefineIn = typeof(IronRubyOps.ClrOps))]
    public static class ClrNameOps {
        [RubyMethod("inspect")]
        public static MutableString/*!*/ Inspect(ClrName/*!*/ self) {
            return ClrString.Inspect(self.MangledName);
        }

        [RubyMethod("dump")]
        public static MutableString/*!*/ Dump(ClrName/*!*/ self) {
            return ClrString.Dump(self.MangledName);
        }

        [RubyMethod("clr_name")]
        public static MutableString/*!*/ GetClrName(ClrName/*!*/ self) {
            return MutableString.Create(self.ActualName);
        }

        [RubyMethod("to_s")]
        [RubyMethod("to_str")]
        [RubyMethod("ruby_name")]
        public static MutableString/*!*/ GetRubyName(ClrName/*!*/ self) {
            return MutableString.Create(self.MangledName);
        }

        [RubyMethod("to_sym")]
        public static SymbolId ToSymbol(ClrName/*!*/ self) {
            return SymbolTable.StringToId(self.MangledName);
        }

        [RubyMethod("==")]
        public static bool IsEqual(ClrName/*!*/ self, [DefaultProtocol, NotNull]string/*!*/ other) {
            return self.MangledName == other;
        }

        [RubyMethod("==")]
        public static bool IsEqual(ClrName/*!*/ self, [NotNull]MutableString/*!*/ other) {
            return self.MangledName == other.ConvertToString();
        }

        [RubyMethod("==")]
        public static bool IsEqual(ClrName/*!*/ self, [NotNull]ClrName/*!*/ other) {
            return self.Equals(other);
        }

        [RubyMethod("<=>")]
        public static int Compare(ClrName/*!*/ self, [DefaultProtocol, NotNull]string/*!*/ other) {
            return Math.Sign(self.MangledName.CompareTo(other));
        }

        [RubyMethod("<=>")]
        public static int Compare(ClrName/*!*/ self, [NotNull]ClrName/*!*/ other) {
            return self.MangledName.CompareTo(other.MangledName);
        }

        [RubyMethod("<=>")]
        public static int Compare(ClrName/*!*/ self, [NotNull]MutableString/*!*/ other) {
            // TODO: do not create MS
            return -Math.Sign(other.CompareTo(MutableString.Create(self.MangledName, RubyEncoding.UTF8)));
        }

        [RubyMethod("<=>")]
        public static object Compare(BinaryOpStorage/*!*/ comparisonStorage, RespondToStorage/*!*/ respondToStorage, ClrName/*!*/ self, object other) {
            return MutableStringOps.Compare(comparisonStorage, respondToStorage, self.MangledName, other);
        }

        /// <summary>
        /// Converts a Ruby name to PascalCase name (e.g. "foo_bar" to "FooBar").
        /// Returns null if the name is not a well-formed Ruby name (it contains upper-case latter or subsequent underscores).
        /// Characters that are not upper case letters are treated as lower-case letters.
        /// </summary>
        [RubyMethod("ruby_to_clr", RubyMethodAttributes.PublicSingleton)]
        [RubyMethod("unmangle", RubyMethodAttributes.PublicSingleton)]
        public static MutableString Unmangle(RubyClass/*!*/ self, [DefaultProtocol]string/*!*/ rubyName) {
            var clr = RubyUtils.TryUnmangleName(rubyName);
            return clr != null ? MutableString.Create(clr) : null;
        }

        /// <summary>
        /// Converts a camelCase or PascalCase name to a Ruby name (e.g. "FooBar" to "foo_bar").
        /// Returns null if the name is not in camelCase or PascalCase (FooBAR, foo, etc.).
        /// Characters that are not upper case letters are treated as lower-case letters.
        /// </summary>
        [RubyMethod("clr_to_ruby", RubyMethodAttributes.PublicSingleton)]
        [RubyMethod("mangle", RubyMethodAttributes.PublicSingleton)]
        public static MutableString Mangle(RubyClass/*!*/ self, [DefaultProtocol]string/*!*/ clrName) {
            var ruby = RubyUtils.TryMangleName(clrName);
            return ruby != null ? MutableString.Create(ruby) : null;
        }
    }
}
