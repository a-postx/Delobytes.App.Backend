using Delobytes.App.Backend.Identity.Api.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Identity.Api.Swagger;

/// <summary>
/// Добавляет значения в пример LoginRequest.
/// </summary>
public class LoginExampleSchemaFilter : ISchemaFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(LoginRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["Email"] = new OpenApiString("241@mail.ru"),
                ["Password"] = new OpenApiString("123"),
            };
        }
    }
}
