// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FunctionalTests
{
    public class Startup
    {
        private readonly SymmetricSecurityKey SecurityKey = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());
        private readonly JwtSecurityTokenHandler JwtTokenHandler = new JwtSecurityTokenHandler();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddConnections();
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
            })
            .AddJsonProtocol(options =>
            {
                // we are running the same tests with JSON and MsgPack protocols and having
                // consistent casing makes it cleaner to verify results
                options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddMessagePackProtocol();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy =>
                {
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireClaim(ClaimTypes.NameIdentifier);
                });
            });

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateActor = false,
                        ValidateLifetime = true,
                        IssuerSigningKey = SecurityKey
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var signalRTokenHeader = context.Request.Query["access_token"];

                            if (!string.IsNullOrEmpty(signalRTokenHeader) &&
                                (context.HttpContext.WebSockets.IsWebSocketRequest || context.Request.Headers["Accept"] == "text/event-stream"))
                            {
                                context.Token = context.Request.Query["access_token"];
                            }
                            return Task.CompletedTask;
                        }
                    };
                });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseFileServer();

            // Custom CORS to allow any origin + credentials (which isn't allowed by the CORS spec)
            // This is for testing purposes only (karma hosts the client on its own server), never do this in production
            app.Use((context, next) =>
            {
                var originHeader = context.Request.Headers[HeaderNames.Origin];
                if (!StringValues.IsNullOrEmpty(originHeader))
                {
                    context.Response.Headers[HeaderNames.AccessControlAllowOrigin] = originHeader;
                    context.Response.Headers[HeaderNames.AccessControlAllowCredentials] = "true";

                    var requestMethod = context.Request.Headers[HeaderNames.AccessControlRequestMethod];
                    if (!StringValues.IsNullOrEmpty(requestMethod))
                    {
                        context.Response.Headers[HeaderNames.AccessControlAllowMethods] = requestMethod;
                    }

                    var requestHeaders = context.Request.Headers[HeaderNames.AccessControlRequestHeaders];
                    if (!StringValues.IsNullOrEmpty(requestHeaders))
                    {
                        context.Response.Headers[HeaderNames.AccessControlAllowHeaders] = requestHeaders;
                    }
                }

                if (string.Equals(context.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Task.CompletedTask;
                }

                return next.Invoke();
            });

            app.UseConnections(routes =>
            {
                routes.MapConnectionHandler<EchoConnectionHandler>("/echo");
            });

            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Value.Contains("/negotiate"))
                {
                    context.Response.Cookies.Append("testCookie", "testValue");
                    context.Response.Cookies.Append("testCookie2", "testValue2");
                    context.Response.Cookies.Append("expiredCookie", "doesntmatter", new CookieOptions() { Expires = DateTimeOffset.Now.AddHours(-1) });
                }
                await next.Invoke();
            });

            app.UseSignalR(routes =>
            {
                routes.MapHub<TestHub>("/testhub");
                routes.MapHub<TestHub>("/testhub-nowebsockets", options => options.Transports = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling);
                routes.MapHub<UncreatableHub>("/uncreatable");
                routes.MapHub<HubWithAuthorization>("/authorizedhub");
            });

            app.Use(next => async (context) =>
            {
                if (context.Request.Path.StartsWithSegments("/generateJwtToken"))
                {
                    await context.Response.WriteAsync(GenerateJwtToken());
                    return;
                }

                if (context.Request.Path.StartsWithSegments("/deployment"))
                {
                    var attributes = Assembly.GetAssembly(typeof(Startup)).GetCustomAttributes<AssemblyMetadataAttribute>();

                    context.Response.ContentType = "application/json";
                    using (var textWriter = new StreamWriter(context.Response.Body))
                    using (var writer = new JsonTextWriter(textWriter))
                    {
                        var json = new JObject();
                        var commitHash = string.Empty;

                        foreach (var attribute in attributes)
                        {
                            json.Add(attribute.Key, attribute.Value);

                            if (string.Equals(attribute.Key, "CommitHash"))
                            {
                                commitHash = attribute.Value;
                            }
                        }

                        if (!string.IsNullOrEmpty(commitHash))
                        {
                            json.Add("GitHubUrl", $"https://github.com/aspnet/SignalR/commit/{commitHash}");
                        }

                        json.WriteTo(writer);
                    }
                }
            });
        }

        private string GenerateJwtToken()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "testuser") };
            var credentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken("SignalRTestServer", "SignalRTests", claims, expires: DateTime.Now.AddSeconds(5), signingCredentials: credentials);
            return JwtTokenHandler.WriteToken(token);
        }
    }
}
