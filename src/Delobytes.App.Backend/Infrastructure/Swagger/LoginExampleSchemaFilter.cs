using Delobytes.App.Backend.Identity.Application.Commands.Login;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Infrastructure.Swagger;

/// <summary>
/// Добавляет значения в пример LoginCommand.
/// </summary>
public class LoginExampleSchemaFilter : ISchemaFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(LoginCommand))
        {
            schema.Example = new OpenApiObject
            {
                ["Email"] = new OpenApiString("241@mail.ru"),
                ["Password"] = new OpenApiString("123"),
                ["IdentityProvider"] = new OpenApiString("Local"),
            };
        }
    }
}
