namespace DotLens.Infrastructure;

/// <summary>
/// Marker attribute indicating a value must be a positive integer (>= 1).
/// Used for parameters like maxResults, limit, line numbers, etc. where 0 is not a valid value.
/// </summary>
/// <remarks>
/// This attribute has two effects:
/// 1. Adds "minimum": 1 to the JSON schema via TransformSchemaNode
/// 2. Validated at runtime by ParameterValidationFilter
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PositiveIntegerAttribute : Attribute;

/// <summary>
/// Marker attribute indicating a value must be a non-negative integer (>= 0).
/// Used for parameters like offset where 0 is valid but negative values are not.
/// </summary>
/// <remarks>
/// This attribute has two effects:
/// 1. Adds "minimum": 0 to the JSON schema via TransformSchemaNode
/// 2. Validated at runtime by ParameterValidationFilter
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NonNegativeAttribute : Attribute;
