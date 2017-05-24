// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public static class AuthorizeHelper
    {
        public static async Task<bool> AuthorizeAsync(HttpContext context, IList<string> policies)
        {
            if (policies.Count == 0)
            {
                return true;
            }

            var policyProvider = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();
            var authorizeData = new List<IAuthorizeData>();

            foreach (var policy in policies)
            {
                authorizeData.Add(new AuthorizeAttribute(policy));
            }

            var authorizePolicy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);

            var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();

            // This will set context.User if required
            var authenticateResult = await policyEvaluator.AuthenticateAsync(authorizePolicy, context);

            var authorizeResult = await policyEvaluator.AuthorizeAsync(authorizePolicy, authenticateResult, context);
            if (authorizeResult.Succeeded)
            {
                return true;
            }
            else if (authorizeResult.Challenged)
            {
                foreach (var scheme in authorizePolicy.AuthenticationSchemes)
                {
                    await context.ChallengeAsync(scheme);
                }
                return false;
            }
            else if (authorizeResult.Forbidden)
            {
                foreach (var scheme in authorizePolicy.AuthenticationSchemes)
                {
                    await context.ForbidAsync(scheme);
                }
                return false;
            }
            return false;
        }
    }
}
