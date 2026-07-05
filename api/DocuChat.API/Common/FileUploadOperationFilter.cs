using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocuChat.API.Common;

public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasFileParam = context.MethodInfo
            .GetParameters()
            .Any(p => p.ParameterType == typeof(IFormFile)
                   || p.ParameterType.GetProperties()
                       .Any(prop => prop.PropertyType == typeof(IFormFile)));

        if (!hasFileParam) return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema { Type = "string", Format = "binary" }
                        },
                        Required = new HashSet<string> { "file" }
                    }
                }
            }
        };
    }
}
