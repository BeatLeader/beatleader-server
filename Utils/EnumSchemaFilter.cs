using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace BeatLeader_Server.Utils {
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type.IsEnum)
            {
                var enumValues = Enum.GetValues(context.Type)
                    .Cast<object>()
                    .Select(e => (JsonNode)string.Concat(e.ToString().Substring(0, 1).ToLower(), e.ToString().AsSpan(1)))
                    .ToList();

                var enumIntValues = Enum.GetValues(context.Type)
                    .Cast<object>()
                    .Select(e => (JsonNode)(int)e)
                    .ToList();

                var writeSchema = (OpenApiSchema)schema;

                writeSchema.OneOf = new List<OpenApiSchema> {
                    new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = enumIntValues },
                    new OpenApiSchema { Type = JsonSchemaType.String, Enum = enumValues }
                }.ToList<IOpenApiSchema>();
                writeSchema.Type = JsonSchemaType.String; 
                writeSchema.Enum = enumValues;

                if (context.Type.GetCustomAttributes(typeof(FlagsAttribute), false).Any()) {
                    writeSchema.Description += "Bitmask enum. Values can be combined using bitwise OR operator.";
                    writeSchema.Example = Enum.GetValues(context.Type)
                        .Cast<object>()
                        .Select(i => (int)i)
                        .Aggregate((a, b) => a | b);
                    
                }
            }
        }
    }
}
