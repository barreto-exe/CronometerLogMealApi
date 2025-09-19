using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CronometerLogMealApi.Swagger;

/// <summary>
/// Expands complex parameters marked with [FromHeader] into individual header inputs in Swagger.
/// Example: [FromHeader] AuthPayload becomes two headers: X-User-Id and X-Auth-Token.
/// </summary>
public sealed class FromHeaderComplexTypeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var parameters = context.MethodInfo.GetParameters();

        foreach (var parameterInfo in parameters)
        {
            var fromHeaderOnParam = parameterInfo.GetCustomAttribute<FromHeaderAttribute>();
            if (fromHeaderOnParam is null)
                continue;

            var paramType = parameterInfo.ParameterType;
            if (IsSimpleType(paramType))
                continue; // Simple types already render correctly as one header

            // Remove the auto-generated complex header parameter if present (e.g. "auth" object)
            var autoName = fromHeaderOnParam.Name ?? parameterInfo.Name;
            var toRemove = operation.Parameters
                .Where(p => p.In == ParameterLocation.Header && string.Equals(p.Name, autoName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var rem in toRemove)
            {
                operation.Parameters.Remove(rem);
            }

            // Add one header parameter for each public instance property
            foreach (var prop in paramType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Only include readable properties
                if (!prop.CanRead)
                    continue;

                var propHeaderAttr = prop.GetCustomAttribute<FromHeaderAttribute>();
                var headerName = propHeaderAttr?.Name ?? prop.Name;

                var schema = context.SchemaGenerator.GenerateSchema(prop.PropertyType, context.SchemaRepository);

                // Determine required: mark value types (non-nullable) and [Required] as required
                var required = IsNonNullableValueType(prop.PropertyType) ||
                               prop.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ComponentModel.DataAnnotations.RequiredAttribute");

                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = headerName,
                    In = ParameterLocation.Header,
                    Required = required,
                    Schema = schema
                });
            }
        }
    }

    private static bool IsSimpleType(Type type)
    {
        if (type.IsPrimitive)
            return true;
        if (type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return true;
        var converter = TypeDescriptor.GetConverter(type);
        return converter != null && converter.CanConvertFrom(typeof(string));
    }

    private static bool IsNonNullableValueType(Type type)
    {
        return type.IsValueType && Nullable.GetUnderlyingType(type) == null;
    }
}
