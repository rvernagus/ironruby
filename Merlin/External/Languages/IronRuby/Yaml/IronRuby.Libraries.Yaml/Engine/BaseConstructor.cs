/***** BEGIN LICENSE BLOCK *****
 * Version: CPL 1.0
 *
 * The contents of this file are subject to the Common Public
 * License Version 1.0 (the "License"); you may not use this file
 * except in compliance with the License. You may obtain a copy of
 * the License at http://www.eclipse.org/legal/cpl-v10.html
 *
 * Software distributed under the License is distributed on an "AS
 * IS" basis, WITHOUT WARRANTY OF ANY KIND, either express or
 * implied. See the License for the specific language governing
 * rights and limitations under the License.
 *
 * Copyright (C) 2007 Ola Bini <ola@ologix.com>
 * Copyright (c) Microsoft Corporation.
 * 
 ***** END LICENSE BLOCK *****/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using IronRuby.Builtins;
using IronRuby.Runtime;
using Microsoft.Scripting.Utils;
using System.Reflection;

namespace IronRuby.StandardLibrary.Yaml {

    public delegate void RecursiveFixer(Node node, object real);
    public delegate object YamlConstructor(BaseConstructor self, Node node);
    public delegate object YamlMultiConstructor(BaseConstructor self, string pref, Node node);

    public class BaseConstructor : IEnumerable<object> {
        private readonly static Dictionary<string, YamlConstructor> _yamlConstructors = new Dictionary<string, YamlConstructor>();
        private readonly static Dictionary<string, YamlMultiConstructor> _yamlMultiConstructors = new Dictionary<string, YamlMultiConstructor>();
        private readonly static Dictionary<string, Regex> _yamlMultiRegexps = new Dictionary<string, Regex>();        
        
        private readonly Dictionary<Node, List<RecursiveFixer>>/*!*/ _recursiveObjects = new Dictionary<Node, List<RecursiveFixer>>();
        private readonly NodeProvider/*!*/ _nodeProvider;
        private readonly RubyGlobalScope/*!*/ _globalScope;

        public BaseConstructor(NodeProvider/*!*/ nodeProvider, RubyGlobalScope/*!*/ globalScope) {
            Assert.NotNull(nodeProvider, globalScope);
            _nodeProvider = nodeProvider;
            _globalScope = globalScope;
        }

        public RubyGlobalScope/*!*/ GlobalScope {
            get { return _globalScope; }
        }

        public virtual YamlConstructor GetYamlConstructor(string key) {
            YamlConstructor result;
            _yamlConstructors.TryGetValue(key, out result);
            return result;
        }

        public virtual YamlMultiConstructor GetYamlMultiConstructor(string key) {
            YamlMultiConstructor result;
            _yamlMultiConstructors.TryGetValue(key, out result);
            return result;
        }

        public virtual Regex GetYamlMultiRegexp(string key) {
            Regex result;
            _yamlMultiRegexps.TryGetValue(key, out result);
            return result;
        }

        public virtual ICollection<string> GetYamlMultiRegexps() {
            return _yamlMultiRegexps.Keys;
        }

        public static void AddConstructor(string tag, YamlConstructor ctor) {
            _yamlConstructors.Add(tag,ctor);
        }

        public static void AddMultiConstructor(string tagPrefix, YamlMultiConstructor ctor) {
            _yamlMultiConstructors.Add(tagPrefix,ctor);
            _yamlMultiRegexps.Add(tagPrefix, new Regex("^" + tagPrefix, RegexOptions.Compiled));
        }

        public bool CheckData() {
            return _nodeProvider.CheckNode();
        }

        public object GetData() {
            if (_nodeProvider.CheckNode()) {
                Node node = _nodeProvider.GetNode();
                if(null != node) {
                    return ConstructDocument(node);
                }
            }
            return null;
        }

        public object ConstructDocument(Node node) {
            object data = ConstructObject(node);
            _recursiveObjects.Clear();
            return data;
        }

        private YamlConstructor yamlMultiAdapter(YamlMultiConstructor ctor, string prefix) {
            return delegate(BaseConstructor self, Node node) { return ctor(self, prefix, node); };
        }

        public static Node GetNullNode() {
            return new ScalarNode("tag:yaml.org,2002:null", null, (char)0);
        }

        public object ConstructObject(Node node) {
            if (node == null) {
                node = GetNullNode();
            }
            if(_recursiveObjects.ContainsKey(node)) {
                return new LinkNode(node);
            }
            _recursiveObjects.Add(node, new List<RecursiveFixer>());
            YamlConstructor ctor = GetYamlConstructor(node.Tag);
            if (ctor == null) {
                bool through = true;
                foreach (string tagPrefix in GetYamlMultiRegexps()) {
                    Regex reg = GetYamlMultiRegexp(tagPrefix);
                    if (reg.IsMatch(node.Tag)) {
                        string tagSuffix = node.Tag.Substring(tagPrefix.Length);
                        ctor = yamlMultiAdapter(GetYamlMultiConstructor(tagPrefix), tagSuffix);
                        through = false;
                        break;
                    }
                }
                if (through) {
                    YamlMultiConstructor xctor = GetYamlMultiConstructor("");
                    if(null != xctor) {
                        ctor = yamlMultiAdapter(xctor,node.Tag);
                    } else {
                        ctor = GetYamlConstructor("");
                        if (ctor == null) {
                            ctor = (s, n) => s.ConstructPrimitive(n);
                        }
                    }
                }
            }
            object data = ctor(this, node);
            DoRecursionFix(node,data);
            return data;
        }

        public void DoRecursionFix(Node node, object obj) {
            List<RecursiveFixer> ll;
            if (_recursiveObjects.TryGetValue(node, out ll)) {
                _recursiveObjects.Remove(node);
                foreach (RecursiveFixer fixer in ll) {
                    fixer(node, obj);
                }
            }
        }

        public object ConstructPrimitive(Node node) {
            if(node is ScalarNode) {
                return ConstructScalar(node);
            } else if(node is SequenceNode) {
                return ConstructSequence(node);            
            } else if(node is MappingNode) {
                return ConstructMapping(node);
            } else {
                Console.Error.WriteLine(node.Tag);
            }
            return null;
        }        

        public object ConstructScalar(Node node) {
            ScalarNode scalar = node as ScalarNode;
            if (scalar == null) {
                MappingNode mapNode = node as MappingNode;
                if (mapNode != null) {
                    foreach (KeyValuePair<Node, Node> entry in mapNode.Nodes) {
                        if ("tag:yaml.org,2002:value" == entry.Key.Tag) {
                            return ConstructScalar(entry.Value);
                        }
                    }
                }
                throw new ConstructorException("expected a scalar or mapping node, but found: " + node);
            }
            string value = scalar.Value;
            if (value.Length > 1 && value[0] == ':' && scalar.Style == '\0') {
                return SymbolTable.StringToId(value.Substring(1));
            }
            return value;
        }        

        public object ConstructPrivateType(Node node) {
            object val = null;
            ScalarNode scalar = node as ScalarNode;
            if (scalar != null) {
                val = scalar.Value;
            } else if (node is MappingNode) {
                val = ConstructMapping(node);
            } else if (node is SequenceNode) {
                val = ConstructSequence(node);
            } else {
                throw new ConstructorException("unexpected node type: " + node);
            }            
            return new PrivateType(node.Tag,val);
        }
        
        public RubyArray ConstructSequence(Node sequenceNode) {
            SequenceNode seq = sequenceNode as SequenceNode;
            if(seq == null) {
                throw new ConstructorException("expected a sequence node, but found: " + sequenceNode);
            }
            IList<Node> @internal = seq.Nodes;
            RubyArray val = new RubyArray(@internal.Count);
            foreach (Node node in @internal) {
                object obj = ConstructObject(node);
                LinkNode linkNode = obj as LinkNode;
                if (linkNode != null) {
                    int ix = val.Count;
                    AddFixer(linkNode.Linked, delegate (Node n, object real) {
                        val[ix] = real;
                    });
                }
                val.Add(obj);
            }
            return val;
        }

        //TODO: remove Ruby-specific stuff from this layer
        public Hash ConstructMapping(Node mappingNode) {
            MappingNode map = mappingNode as MappingNode;
            if (map == null) {
                throw new ConstructorException("expected a mapping node, but found: " + mappingNode);
            }
            Hash mapping = new Hash(_globalScope.Context);
            LinkedList<Hash> merge = null;
            foreach (KeyValuePair<Node, Node> entry in map.Nodes) {
                Node key_v = entry.Key;
                Node value_v = entry.Value;

                if (key_v.Tag == "tag:yaml.org,2002:merge") {
                    if (merge != null) {
                        throw new ConstructorException("while constructing a mapping: found duplicate merge key");
                    }
                    SequenceNode sequence;
                    merge = new LinkedList<Hash>();
                    if (value_v is MappingNode) {
                        merge.AddLast(ConstructMapping(value_v));
                    } else if ((sequence = value_v as SequenceNode) != null) {
                        foreach (Node subNode in sequence.Nodes) {
                            if (!(subNode is MappingNode)) {
                                throw new ConstructorException("while constructing a mapping: expected a mapping for merging, but found: " + subNode);
                            }
                            merge.AddFirst(ConstructMapping(subNode));
                        }
                    } else {
                        throw new ConstructorException("while constructing a mapping: expected a mapping or list of mappings for merging, but found: " + value_v);
                    }
                } else if (key_v.Tag == "tag:yaml.org,2002:value") {
                    if(mapping.ContainsKey("=")) {
                        throw new ConstructorException("while construction a mapping: found duplicate value key");
                    }
                    mapping.Add("=", ConstructObject(value_v));
                } else {
                    object kk = ConstructObject(key_v);
                    object vv = ConstructObject(value_v);
                    LinkNode linkNode = vv as LinkNode;
                    if (linkNode != null) {
                        AddFixer(linkNode.Linked, delegate (Node node, object real) {
                            IDictionaryOps.SetElement(_globalScope.Context, mapping, kk, real);
                        });
                    }
                    IDictionaryOps.SetElement(_globalScope.Context, mapping, kk, vv);
                }
            }
            if (null != merge) {
                merge.AddLast(mapping);
                mapping = new Hash(_globalScope.Context);
                foreach (Hash m in merge) {                    
                    foreach (KeyValuePair<object, object> e in m) {
                        IDictionaryOps.SetElement(_globalScope.Context, mapping, e.Key, e.Value);
                    }
                }
            }
            return mapping;
        }

        public void AddFixer(Node node, RecursiveFixer fixer) {
            List<RecursiveFixer> ll;
            if (!_recursiveObjects.TryGetValue(node, out ll)) {
                _recursiveObjects.Add(node, ll = new List<RecursiveFixer>());
            }
            ll.Add(fixer);
        }

        public object ConstructPairs(Node mappingNode) {
            MappingNode map = mappingNode as MappingNode;
            if (map == null) {
                throw new ConstructorException("expected a mapping node, but found: " + mappingNode);
            }

            List<object[]> result = new List<object[]>();
            foreach (KeyValuePair<Node, Node> entry in map.Nodes) {
                result.Add(new object[] { ConstructObject(entry.Key), ConstructObject(entry.Value) });
            }
            return result;
        }

        #region IEnumerable<object> Members

        public IEnumerator<object> GetEnumerator() {
            while (CheckData()) {
                yield return GetData();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        #endregion

        #region Statics

        private static readonly Dictionary<string, bool> BOOL_VALUES;

        static BaseConstructor() {
            BOOL_VALUES = new Dictionary<string, bool>();
            BOOL_VALUES.Add("yes", true);
            BOOL_VALUES.Add("Yes", true);
            BOOL_VALUES.Add("YES", true);
            BOOL_VALUES.Add("no", false);
            BOOL_VALUES.Add("No", false);
            BOOL_VALUES.Add("NO", false);
            BOOL_VALUES.Add("true", true);
            BOOL_VALUES.Add("True", true);
            BOOL_VALUES.Add("TRUE", true);
            BOOL_VALUES.Add("false", false);
            BOOL_VALUES.Add("False", false);
            BOOL_VALUES.Add("FALSE", false);
            BOOL_VALUES.Add("on", true);
            BOOL_VALUES.Add("On", true);
            BOOL_VALUES.Add("ON", true);
            BOOL_VALUES.Add("off", false);
            BOOL_VALUES.Add("Off", false);
            BOOL_VALUES.Add("OFF", false);

            AddConstructor("tag:yaml.org,2002:null", ConstructYamlNull);
            AddConstructor("tag:yaml.org,2002:bool", ConstructYamlBool);
            AddConstructor("tag:yaml.org,2002:omap", ConstructYamlOmap);
            AddConstructor("tag:yaml.org,2002:pairs", ConstructYamlPairs);
            AddConstructor("tag:yaml.org,2002:set", ConstructYamlSet);
            AddConstructor("tag:yaml.org,2002:int", ConstructYamlInt);
            AddConstructor("tag:yaml.org,2002:float", ConstructYamlFloat);
            AddConstructor("tag:yaml.org,2002:timestamp", ConstructYamlTimestamp);
            AddConstructor("tag:yaml.org,2002:timestamp#ymd", ConstructYamlTimestampYMD);
            AddConstructor("tag:yaml.org,2002:str", ConstructYamlStr);
            AddConstructor("tag:yaml.org,2002:binary", ConstructYamlBinary);
            AddConstructor("tag:yaml.org,2002:seq", ConstructYamlSeq);
            AddConstructor("tag:yaml.org,2002:map", ConstructYamlMap);
            AddConstructor("", (self, node) => self.ConstructPrivateType(node));
            AddMultiConstructor("tag:yaml.org,2002:seq:", ConstructSpecializedSequence);
            AddMultiConstructor("tag:yaml.org,2002:map:", ConstructSpecializedMap);
            AddMultiConstructor("!cli/object:", ConstructCliObject);
            AddMultiConstructor("tag:cli.yaml.org,2002:object:", ConstructCliObject);
        }

        public static object ConstructYamlNull(BaseConstructor ctor, Node node) {
            return null;
        }

        public static object ConstructYamlBool(BaseConstructor ctor, Node node) {
            bool result;
            if (TryConstructYamlBool(ctor, node, out result)) {
                return result;
            }
            return null;
        }

        public static bool TryConstructYamlBool(BaseConstructor ctor, Node node, out bool result) {
            if (BOOL_VALUES.TryGetValue(ctor.ConstructScalar(node).ToString(), out result)) {
                return true;
            }
            return false;
        }

        public static object ConstructYamlOmap(BaseConstructor ctor, Node node) {
            return ctor.ConstructPairs(node);
        }

        public static object ConstructYamlPairs(BaseConstructor ctor, Node node) {
            return ConstructYamlOmap(ctor, node);
        }

        public static ICollection ConstructYamlSet(BaseConstructor ctor, Node node) {
            return ctor.ConstructMapping(node).Keys;
        }

        public static string ConstructYamlStr(BaseConstructor ctor, Node node) {
            string value = ctor.ConstructScalar(node).ToString();
            return value.Length != 0 ? value : null;
        }

        public static RubyArray ConstructYamlSeq(BaseConstructor ctor, Node node) {
            return ctor.ConstructSequence(node);
        }

        public static Hash ConstructYamlMap(BaseConstructor ctor, Node node) {
            return ctor.ConstructMapping(node);
        }

        public static object constructUndefined(BaseConstructor ctor, Node node) {
            throw new ConstructorException("could not determine a constructor for the tag: " + node.Tag);
        }

        private static Regex TIMESTAMP_REGEXP = new Regex("^(-?[0-9][0-9][0-9][0-9])-([0-9][0-9]?)-([0-9][0-9]?)(?:(?:[Tt]|[ \t]+)([0-9][0-9]?):([0-9][0-9]):([0-9][0-9])(?:\\.([0-9]*))?(?:[ \t]*(Z|([-+][0-9][0-9]?)(?::([0-9][0-9])?)?)))?$");
        internal static Regex YMD_REGEXP = new Regex("^(-?[0-9][0-9][0-9][0-9])-([0-9][0-9]?)-([0-9][0-9]?)$");

        public static object ConstructYamlTimestampYMD(BaseConstructor ctor, Node node) {
            ScalarNode scalar = node as ScalarNode;
            if (scalar == null) {
                throw new ConstructorException("can only contruct timestamp from scalar node");
            }

            Match match = YMD_REGEXP.Match(scalar.Value);
            if (match.Success) {
                int year_ymd = int.Parse(match.Groups[1].Value);
                int month_ymd = int.Parse(match.Groups[2].Value);
                int day_ymd = int.Parse(match.Groups[3].Value);

                return new DateTime(year_ymd, month_ymd, day_ymd);
            }
            throw new ConstructorException("Invalid tag:yaml.org,2002:timestamp#ymd value.");
        }

        public static object ConstructYamlTimestamp(BaseConstructor ctor, Node node) {
            ScalarNode scalar = node as ScalarNode;
            if (scalar == null) {
                throw new ConstructorException("can only contruct timestamp from scalar node");
            }

            Match match = TIMESTAMP_REGEXP.Match(scalar.Value);

            if (!match.Success) {
                return ctor.ConstructPrivateType(node);
            }

            string year_s = match.Groups[1].Value;
            string month_s = match.Groups[2].Value;
            string day_s = match.Groups[3].Value;
            string hour_s = match.Groups[4].Value;
            string min_s = match.Groups[5].Value;
            string sec_s = match.Groups[6].Value;
            string fract_s = match.Groups[7].Value;
            string utc = match.Groups[8].Value;
            string timezoneh_s = match.Groups[9].Value;
            string timezonem_s = match.Groups[10].Value;

            bool isUtc = utc == "Z" || utc == "z";

            DateTime dt = new DateTime(
                year_s != "" ? int.Parse(year_s) : 0,
                month_s != "" ? int.Parse(month_s) : 1,
                day_s != "" ? int.Parse(day_s) : 1,
                hour_s != "" ? int.Parse(hour_s) : 0,
                min_s != "" ? int.Parse(min_s) : 0,
                sec_s != "" ? int.Parse(sec_s) : 0,
                isUtc ? DateTimeKind.Utc : DateTimeKind.Local
            );

            if (!string.IsNullOrEmpty(fract_s)) {
                long fract = int.Parse(fract_s);
                if (fract > 0) {
                    while (fract < 1000000) {
                        fract *= 10;
                    }
                    dt = dt.AddTicks(fract);
                }
            }

            if (!isUtc) {
                if (timezoneh_s != "" || timezonem_s != "") {
                    int zone = 0;
                    int sign = +1;
                    if (timezoneh_s != "") {
                        if (timezoneh_s.StartsWith("-")) {
                            sign = -1;
                        }
                        zone += int.Parse(timezoneh_s.Substring(1)) * 3600000;
                    }
                    if (timezonem_s != "") {
                        zone += int.Parse(timezonem_s) * 60000;
                    }
                    double utcOffset = TimeZone.CurrentTimeZone.GetUtcOffset(dt).TotalMilliseconds;
                    dt = dt.AddMilliseconds(utcOffset - sign * zone);
                }
            }
            return dt;
        }

        public static object ConstructYamlInt(BaseConstructor ctor, Node node) {
            string value = ctor.ConstructScalar(node).ToString().Replace("_", "").Replace(",", "");
            int sign = +1;
            char first = value[0];
            if (first == '-') {
                sign = -1;
                value = value.Substring(1);
            } else if (first == '+') {
                value = value.Substring(1);
            }
            int @base = 10;
            if (value == "0") {
                return 0;
            } else if (value.StartsWith("0b")) {
                value = value.Substring(2);
                @base = 2;
            } else if (value.StartsWith("0x")) {
                value = value.Substring(2);
                @base = 16;
            } else if (value.StartsWith("0")) {
                value = value.Substring(1);
                @base = 8;
            } else if (value.IndexOf(':') != -1) {
                string[] digits = value.Split(':');
                int bes = 1;
                int val = 0;
                for (int i = 0, j = digits.Length; i < j; i++) {
                    val += (int.Parse(digits[(j - i) - 1]) * bes);
                    bes *= 60;
                }
                return sign * val;
            }

            try {
                // LiteralParser.ParseInteger delegate handles parsing & conversion to BigInteger (if needed)
                return LiteralParser.ParseInteger(sign, value, @base);
            } catch (Exception e) {
                throw new ConstructorException(string.Format("Could not parse integer value: '{0}' (sign {1}, base {2})", value, sign, @base), e);
            }
        }

        public static object ConstructYamlFloat(BaseConstructor ctor, Node node) {
            string value = ctor.ConstructScalar(node).ToString().Replace("_", "").Replace(",", "");
            int sign = +1;
            char first = value[0];
            if (first == '-') {
                sign = -1;
                value = value.Substring(1);
            } else if (first == '+') {
                value = value.Substring(1);
            }
            string valLower = value.ToLower();
            if (valLower == ".inf") {
                return sign == -1 ? double.NegativeInfinity : double.PositiveInfinity;
            } else if (valLower == ".nan") {
                return double.NaN;
            } else if (value.IndexOf(':') != -1) {
                string[] digits = value.Split(':');
                int bes = 1;
                double val = 0.0;
                for (int i = 0, j = digits.Length; i < j; i++) {
                    val += (double.Parse(digits[(j - i) - 1]) * bes);
                    bes *= 60;
                }
                return sign * val;
            } else {
                return sign * double.Parse(value);
            }
        }

        public static byte[] ConstructYamlBinary(BaseConstructor ctor, Node node) {
            string val = ctor.ConstructScalar(node).ToString().Replace("\r", "").Replace("\n", "");
            return Convert.FromBase64String(val);
        }

        public static object ConstructSpecializedSequence(BaseConstructor ctor, string pref, Node node) {
            RubyArray result = null;
            try {
                result = (RubyArray)Type.GetType(pref).GetConstructor(Type.EmptyTypes).Invoke(null);
            } catch (Exception e) {
                throw new ConstructorException("Can't construct a sequence from class: " + pref, e);
            }
            foreach (object x in ctor.ConstructSequence(node)) {
                result.Add(x);
            }
            return result;
        }

        public static object ConstructSpecializedMap(BaseConstructor ctor, string pref, Node node) {
            Hash result = null;
            try {
                result = (Hash)Type.GetType(pref).GetConstructor(Type.EmptyTypes).Invoke(null);
            } catch (Exception e) {
                throw new ConstructorException("Can't construct a mapping from class: " + pref, e);
            }
            foreach (KeyValuePair<object, object> e in ctor.ConstructMapping(node)) {
                result.Add(e.Key, e.Value);
            }
            return result;
        }

        public static object ConstructCliObject(BaseConstructor ctor, string pref, Node node) {
            // TODO: should this use serialization or some more standard CLR mechanism?
            //       (it is very ad-hoc)
            // TODO: use DLR APIs instead of reflection
            try {
                Type type = Type.GetType(pref);
                object result = type.GetConstructor(Type.EmptyTypes).Invoke(null);

                foreach (KeyValuePair<object, object> e in ctor.ConstructMapping(node)) {
                    string name = e.Key.ToString();
                    name = "" + char.ToUpper(name[0]) + name.Substring(1);
                    PropertyInfo prop = type.GetProperty(name);

                    prop.SetValue(result, Convert.ChangeType(e.Value, prop.PropertyType), null);
                }
                return result;

            } catch (Exception e) {
                throw new ConstructorException("Can't construct a CLI object from class: " + pref, e);
            }
        }

        #endregion
    }
}
