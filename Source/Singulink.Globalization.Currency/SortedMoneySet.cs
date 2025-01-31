﻿using System.Collections;
using System.Text;

namespace Singulink.Globalization;

/// <summary>
/// Represents a set of money that can contain values in multiple currencies.
/// </summary>
public class SortedMoneySet : IReadOnlyMoneySet, IFormattable
{
    private readonly CurrencyRegistry _registry;
    private readonly SortedDictionary<Currency, decimal> _amountLookup = new(CurrencyByCodeComparer.Default);

    /// <summary>
    /// Initializes a new instance of the <see cref="SortedMoneySet"/> class with the <see cref="CurrencyRegistry.Default"/> currency registry.
    /// </summary>
    public SortedMoneySet() : this(CurrencyRegistry.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SortedMoneySet"/> class with the specified currency registry.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Attempted to add a value with a currency that is not available in the currency registry.
    /// </exception>
    public SortedMoneySet(CurrencyRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc cref="ImmutableSortedMoneySet(Money[])"/>
    public SortedMoneySet(IEnumerable<Money> values) : this(CurrencyRegistry.Default, values)
    {
    }

    /// <inheritdoc cref="ImmutableSortedMoneySet(CurrencyRegistry?, Money[])"/>
    public SortedMoneySet(CurrencyRegistry registry, IEnumerable<Money> values) : this(registry, values, values is not IReadOnlyMoneySet s || s.Registry != registry)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SortedMoneySet"/> class. Trusted internal constructor.
    /// </summary>
    internal SortedMoneySet(CurrencyRegistry registry, IEnumerable<Money> values, bool ensureValuesInRegistry) : this(registry)
    {
        AddRangeInternal(values, ensureValuesInRegistry);
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.this[string]"/>
    public Money this[string currencyCode]
    {
        get {
            var currency = _registry[currencyCode];

            if (_amountLookup.TryGetValue(currency, out decimal amount))
                return new Money(amount, currency);

            return default;
        }
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.this[Currency]"/>
    public Money this[Currency currency]
    {
        get {
            EnsureCurrencyAllowed(currency, nameof(currency));

            if (_amountLookup.TryGetValue(currency, out decimal amount))
                return new Money(amount, currency);

            return default;
        }
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.Count"/>
    public int Count => _amountLookup.Count;

    /// <inheritdoc cref="IReadOnlyMoneySet.Currencies"/>
    public IReadOnlyCollection<Currency> Currencies => _amountLookup.Keys;

    /// <inheritdoc cref="IReadOnlyMoneySet.Registry"/>
    public CurrencyRegistry Registry => _registry;

    /// <summary>
    /// Adds the specified value to this set.
    /// </summary>
    /// <remarks>
    /// Default values that are not associated with any currency are ignored.
    /// </remarks>
    public void Add(Money value)
    {
        var currency = value.CurrencyOrDefault;

        if (currency == null)
            return;

        EnsureCurrencyAllowed(currency, nameof(value));
        AddInternal(value.Amount, currency);
    }

    /// <summary>
    /// Adds the specified currency and amount to this set.
    /// </summary>
    public void Add(decimal amount, string currencyCode)
    {
        var currency = _registry[currencyCode];
        AddInternal(amount, currency);
    }

    /// <summary>
    /// Adds the specified currency and amount to this set.
    /// </summary>
    public void Add(decimal amount, Currency currency)
    {
        EnsureCurrencyAllowed(currency, nameof(currency));
        AddInternal(amount, currency);
    }

    /// <summary>
    /// Adds the specified values to this set.
    /// </summary>
    /// <remarks>
    /// Default values that are not associated with any currency are ignored.
    /// </remarks>
    public void AddRange(IEnumerable<Money> values)
    {
        bool ensureCurrenciesInRegistry = values is not IReadOnlyMoneySet s || s.Registry != _registry;
        AddRangeInternal(values, ensureCurrenciesInRegistry);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the values in this set.
    /// </summary>
    public Enumerator GetEnumerator() => new(_amountLookup);

    /// <summary>
    /// Removes the value with the given currency code.
    /// </summary>
    public bool Remove(string currencyCode)
    {
        var currency = _registry[currencyCode];
        return _amountLookup.Remove(currency);
    }

    /// <summary>
    /// Removes the value with the given currency.
    /// </summary>
    public bool Remove(Currency currency)
    {
        bool removed = _amountLookup.Remove(currency);

        if (!removed)
        {
            EnsureCurrencyAllowed(currency, nameof(currency));
        }

        return removed;
    }

    /// <summary>
    /// Removes all the values from this set that match the specified currencies.
    /// </summary>
    public int RemoveAll(IEnumerable<Currency> currencies)
    {
        int count = 0;

        foreach (var currency in currencies)
        {
            if (Remove(currency))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Copies the values in this set to a new immutable set that uses the same registry as this set.
    /// </summary>
    public ImmutableSortedMoneySet ToImmutableSet() => new ImmutableSortedMoneySet(_registry, this, false);

    /// <summary>
    /// Returns a string representation of the money values this set contains.
    /// </summary>
    public override string ToString() => ToString(null, null);

    /// <summary>
    /// Returns a string representation of the money values this set contains.
    /// </summary>
    /// <param name="format">The format to use for each money value. See <see cref="Money.ToString(string?, IFormatProvider?)"/> for valid money formats.
    /// Prepend the format with the <c>"!"</c> character to ignore zero amount values.</param>
    /// <param name="formatProvider">The format provider that will be used to obtain number format information. This should be a <see cref="CultureInfo"/>
    /// instance for formats that depend on the culture, otherwise the current culture is used.</param>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        bool ignoreZeroAmounts;
        int count;

        if (format != null && format.StartsWith('!'))
        {
            format = format[1..];
            ignoreZeroAmounts = true;
            count = GetNonZeroCount();
        }
        else
        {
            ignoreZeroAmounts = false;
            count = Count;
        }

        if (count == 0)
            return string.Empty;

        var sb = new StringBuilder(count * 8);
        bool first = true;

        foreach (var value in this)
        {
            if (ignoreZeroAmounts && value.Amount == 0)
                continue;

            if (first)
                first = false;
            else
                sb.Append(", ");

            sb.Append(value.ToString(format, formatProvider));
        }

        return sb.ToString();

        int GetNonZeroCount()
        {
            int count = 0;

            foreach (var amount in _amountLookup.Values)
            {
                if (amount != 0)
                    count++;
            }

            return count;
        }
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.TryGetAmount(Currency, out decimal)"/>
    public bool TryGetAmount(Currency currency, out decimal amount)
    {
        return _amountLookup.TryGetValue(currency, out amount);
    }

    public bool TryGetAmount(string currencyCode, out decimal amount)
    {
        var currency = _registry[currencyCode];
        return _amountLookup.TryGetValue(currency, out amount);
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.TryGetValue(Currency, out Money)"/>
    public bool TryGetValue(Currency currency, out Money value)
    {
        EnsureCurrencyAllowed(currency, nameof(currency));

        if (_amountLookup.TryGetValue(currency, out decimal amount))
        {
            value = new Money(amount, currency);
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc cref="IReadOnlyMoneySet.TryGetValue(string, out Money)"/>
    public bool TryGetValue(string currencyCode, out Money value)
    {
        var currency = _registry[currencyCode];
        return TryGetValue(currency, out value);
    }

    private void AddInternal(decimal amount, Currency currency)
    {
        if (_amountLookup.TryGetValue(currency, out decimal existingAmount))
        {
            _amountLookup[currency] = existingAmount + amount;
        }
        else
        {
            _amountLookup[currency] = amount;
        }
    }

    private void AddRangeInternal(IEnumerable<Money> values, bool ensureCurrenciesInRegistry)
    {
        foreach (var value in values)
        {
            var currency = value.CurrencyOrDefault;

            if (currency == null)
                continue;

            if (ensureCurrenciesInRegistry)
                EnsureCurrencyAllowed(currency, nameof(values));

            if (_amountLookup.TryGetValue(currency, out decimal existingAmount))
                _amountLookup[currency] = existingAmount + value.Amount;
            else
                _amountLookup.Add(currency, value.Amount);
        }
    }

    private void EnsureCurrencyAllowed(Currency currency, string paramName)
    {
        if (!_registry.Contains(currency))
            Throw(currency, paramName);

        static void Throw(Currency currency, string paramName)
        {
            throw new ArgumentException($"The currency '{currency}' is not present in the set's currency registry.", paramName);
        }
    }

    #region Explicit Interface Implementations

    /// <inheritdoc cref="IReadOnlyMoneySet.Currencies"/>
    IEnumerable<Currency> IReadOnlyMoneySet.Currencies => Currencies;

    /// <inheritdoc cref="GetEnumerator"/>
    IEnumerator<Money> IEnumerable<Money>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc cref="GetEnumerator"/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion

    /// <summary>
    /// Enumerates the elements of a <see cref="SortedMoneySet"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<Money>
    {
        private SortedDictionary<Currency, decimal>.Enumerator _amountLookupEnumerator;

        /// <summary>
        /// Gets the element at the current position of the enumerator.
        /// </summary>
        public Money Current => new(_amountLookupEnumerator.Current.Value, _amountLookupEnumerator.Current.Key);

        /// <inheritdoc cref="Current"/>
        object? IEnumerator.Current => Current;

        internal Enumerator(SortedDictionary<Currency, decimal> amountLookup)
        {
            _amountLookupEnumerator = amountLookup.GetEnumerator();
        }

        /// <summary>
        /// Releases all the resources used by the enumerator.
        /// </summary>
        public void Dispose() => _amountLookupEnumerator.Dispose();

        /// <summary>
        /// Advances the enumerator to the next element.
        /// </summary>
        public bool MoveNext() => _amountLookupEnumerator.MoveNext();

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <exception cref="NotSupportedException">This operation is not supported.</exception>
        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}