using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatLeader_Server.Utils {
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type.IsEnum)
            {
                var enumValues = Enum.GetValues(context.Type)
                    .Cast<object>()
                    .Select(e => new OpenApiString(e.ToString().Substring(0, 1).ToLower() + e.ToString().Substring(1)))
                    .ToList<IOpenApiAny>();

                var enumIntValues = Enum.GetValues(context.Type)
                    .Cast<object>()
                    .Select(e => new OpenApiInteger((int)e))
                    .ToList<IOpenApiAny>();

                schema.OneOf = new List<OpenApiSchema>
                    {
                        new OpenApiSchema { Type = "integer", Enum = enumIntValues },
                        new OpenApiSchema { Type = "string", Enum = enumValues }
                    };
                    schema.Type = "string"; 
                    schema.Enum = enumValues;

                if (context.Type.GetCustomAttributes(typeof(FlagsAttribute), false).Any())
                {
                    schema.Description += "Bitmask enum. Values can be combined using bitwise OR operator.";
                    schema.Example = new OpenApiInteger(enumIntValues
                        .Cast<OpenApiInteger>()
                        .Select(i => i.Value)
                        .Aggregate((a, b) => a | b));
                    
                }
            }
        }
    }
}
