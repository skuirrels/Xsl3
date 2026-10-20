// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Accumulates a list of sort keys and stores them in an array.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct XmlSortKeyAccumulator
    {
        private XmlSortKey?[] _keys;
        private int _pos;

        // Xsl3 (P4): the collation keys made in this sort, by collation and string. Sort values repeat (the same
        // customer on many orders), and making a collation key is the costly part of sorting strings.
        private System.Collections.Generic.Dictionary<(XmlCollation, string), object>? _collationKeys;

#if DEBUG
        private const int DefaultSortKeyCount = 4;
#else
        private const int DefaultSortKeyCount = 64;
#endif

        /// <summary>
        /// Initialize the XmlSortKeyAccumulator.
        /// </summary>
        public void Create()
        {
            _keys ??= new XmlSortKey[DefaultSortKeyCount];

            _pos = 0;
            _keys[0] = null;
            _collationKeys = null;
        }

        /// <summary>
        /// Create a new sort key and append it to the current run of sort keys.
        /// </summary>
        public void AddStringSortKey(XmlCollation collation, string value)
        {
            _collationKeys ??= new System.Collections.Generic.Dictionary<(XmlCollation, string), object>();
            if (!_collationKeys.TryGetValue((collation, value), out object? data))
            {
                data = collation.CreateSortKeyData(value);
                _collationKeys[(collation, value)] = data;
            }
            AppendSortKey(collation.CreateSortKey(data));
        }

        public void AddDecimalSortKey(XmlCollation collation, decimal value)
        {
            AppendSortKey(new XmlDecimalSortKey(value, collation));
        }

        public void AddIntegerSortKey(XmlCollation collation, long value)
        {
            AppendSortKey(new XmlIntegerSortKey(value, collation));
        }

        public void AddIntSortKey(XmlCollation collation, int value)
        {
            AppendSortKey(new XmlIntSortKey(value, collation));
        }

        public void AddDoubleSortKey(XmlCollation collation, double value)
        {
            AppendSortKey(new XmlDoubleSortKey(value, collation));
        }

        public void AddDateTimeSortKey(XmlCollation collation, DateTime value)
        {
            AppendSortKey(new XmlDateTimeSortKey(value, collation));
        }

        public void AddEmptySortKey(XmlCollation collation)
        {
            AppendSortKey(new XmlEmptySortKey(collation));
        }

        /// <summary>
        /// Finish creating the current run of sort keys and begin a new run.
        /// </summary>
        public void FinishSortKeys()
        {
            _pos++;
            if (_pos >= _keys.Length)
            {
                XmlSortKey[] keysNew = new XmlSortKey[_pos * 2];
                Array.Copy(_keys, keysNew, _keys.Length);
                _keys = keysNew;
            }
            _keys[_pos] = null;
        }

        /// <summary>
        /// Append new sort key to the current run of sort keys.
        /// </summary>
        private void AppendSortKey(XmlSortKey key)
        {
            // Ensure that sort will be stable by setting index of key
            key.Priority = _pos;

            if (_keys[_pos] == null)
                _keys[_pos] = key;
            else
                _keys[_pos]!.AddSortKey(key);
        }

        /// <summary>
        /// Get array of sort keys that was constructed by this internal class.
        /// </summary>
        public Array Keys
        {
            get { return _keys; }
        }
    }
}
