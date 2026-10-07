using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeBrix.PostgresClient.Tests.Support;

/// <summary>
/// The value-equality rules the original test suite's equality assertions relied on, for the places
/// where a plain <c>Equals</c> (and therefore <c>Should().Be(...)</c>) is not enough:
/// <list type="bullet">
/// <item>arrays and other non-string sequences compare element by element (recursively), and
/// multi-dimensional arrays also compare their rank and per-dimension lengths;</item>
/// <item>numbers of different CLR types compare by value (<c>1</c> equals <c>1L</c> and <c>1m</c>);</item>
/// <item>dictionaries compare as sets of key/value pairs.</item>
/// </list>
/// Everything else falls back to <see cref="object.Equals(object, object)"/>.
/// </summary>
public static class ValueEquality
{
    /// <summary>
    /// True when <paramref name="expected"/> and <paramref name="actual"/> are equal under the rules
    /// described on <see cref="ValueEquality"/>.
    /// </summary>
    /// <param name="expected">The expected value.</param>
    /// <param name="actual">The actual value.</param>
    /// <returns>True when the values are equal.</returns>
    public static bool AreEqual(object expected, object actual)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null;

        if (ReferenceEquals(expected, actual))
            return true;

        if (IsNumeric(expected) && IsNumeric(actual))
            return NumericEquals(expected, actual);

        if (expected is string || actual is string)
            return expected.Equals(actual);

        if (expected is Array expectedArray && actual is Array actualArray && (expectedArray.Rank > 1 || actualArray.Rank > 1))
        {
            if (expectedArray.Rank != actualArray.Rank)
                return false;
            for (var dimension = 0; dimension < expectedArray.Rank; dimension++)
                if (expectedArray.GetLength(dimension) != actualArray.GetLength(dimension))
                    return false;
            return SequenceEqual(expectedArray, actualArray);
        }

        if (expected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
        {
            if (expectedDictionary.Count != actualDictionary.Count)
                return false;
            foreach (DictionaryEntry entry in expectedDictionary)
                if (!actualDictionary.Contains(entry.Key) || !AreEqual(entry.Value, actualDictionary[entry.Key]))
                    return false;
            return true;
        }

        if (expected is IEnumerable expectedSequence && actual is IEnumerable actualSequence
            && !(expected is IStructuralEquatable && expected.Equals(actual)))
        {
            return SequenceEqual(expectedSequence, actualSequence);
        }

        return expected.Equals(actual) || actual.Equals(expected);
    }

    /// <summary>
    /// Formats a value for an assertion message, expanding sequences.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A readable rendering of the value.</returns>
    public static string Format(object value)
        => value switch
        {
            null => "null",
            string s => "\"" + s + "\"",
            IEnumerable sequence => "[" + string.Join(", ", sequence.Cast<object>().Select(Format)) + "]",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    static bool SequenceEqual(IEnumerable expected, IEnumerable actual)
    {
        var e = expected.GetEnumerator();
        var a = actual.GetEnumerator();
        while (true)
        {
            var hasExpected = e.MoveNext();
            var hasActual = a.MoveNext();
            if (hasExpected != hasActual)
                return false;
            if (!hasExpected)
                return true;
            if (!AreEqual(e.Current, a.Current))
                return false;
        }
    }

    static bool IsNumeric(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    static bool NumericEquals(object expected, object actual)
    {
        if (expected is float or double || actual is float or double)
        {
            var x = Convert.ToDouble(expected, CultureInfo.InvariantCulture);
            var y = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
            return x.Equals(y);
        }

        if (expected is ulong || actual is ulong)
        {
            try
            {
                return Convert.ToUInt64(expected, CultureInfo.InvariantCulture) == Convert.ToUInt64(actual, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return Convert.ToDecimal(expected, CultureInfo.InvariantCulture) == Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
    }
}
