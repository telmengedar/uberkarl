namespace Uberkarl.Behavior;

/// <summary>One numeric, gamepad-tunable parameter of a predefined behavior (design #7704 §10.5 / #8049 §6.2).</summary>
public sealed class PredefinedParameterDescriptor
{
    public PredefinedParameterDescriptor(string name, double defaultValue, double min, double max, double increment)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name must not be empty.", nameof(name));
        if (min > max)
            throw new ArgumentException("Min must not exceed max.", nameof(min));
        if (increment <= 0)
            throw new ArgumentOutOfRangeException(nameof(increment));
        if (defaultValue < min || defaultValue > max)
            throw new ArgumentOutOfRangeException(nameof(defaultValue), "Default must lie within [min, max].");

        Name = name;
        Default = defaultValue;
        Min = min;
        Max = max;
        Increment = increment;
    }

    /// <summary>The parameter key, matching the name <see cref="PredefinedBehaviors.TryGetSource"/> reads it under.</summary>
    public string Name { get; }

    /// <summary>The value substituted when a binding carries no explicit override for this parameter.</summary>
    public double Default { get; }

    /// <summary>The lower bound a stepper clamps to.</summary>
    public double Min { get; }

    /// <summary>The upper bound a stepper clamps to.</summary>
    public double Max { get; }

    /// <summary>The amount one stepper press moves the value.</summary>
    public double Increment { get; }

    /// <summary>The value one stepper press in <paramref name="direction"/> reaches from <paramref name="current"/>, clamped to <see cref="Min"/>/<see cref="Max"/>.</summary>
    public double Step(double current, int direction) => Math.Clamp(current + Math.Sign(direction) * Increment, Min, Max);
}
