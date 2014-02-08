﻿using System;
using System.Collections.Specialized;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using Thinktecture.IdentityServer.Core.Authentication;
using Thinktecture.IdentityServer.Core.Connect.Models;
using Thinktecture.IdentityServer.Core.Services;

namespace Thinktecture.IdentityServer.Core.Connect
{
    [RoutePrefix("connect/authorize")]
    [HostAuthentication("idsrv")]
    public class AuthorizeEndpointController : ApiController
    {
        private ILogger _logger;

        private AuthorizeRequestValidator _validator;
        private AuthorizeResponseGenerator _responseGenerator;
        private AuthorizeInteractionResponseGenerator _interactionGenerator;
        private ICoreSettings _settings;

        public AuthorizeEndpointController(ILogger logger, AuthorizeRequestValidator validator, AuthorizeResponseGenerator responseGenerator, AuthorizeInteractionResponseGenerator interactionGenerator, ICoreSettings settings)
        {
            _logger = logger;
            _settings = settings;

            _responseGenerator = responseGenerator;
            _interactionGenerator = interactionGenerator;

            _validator = validator;
        }

        [Route]
        public async Task<IHttpActionResult> Get(HttpRequestMessage request)
        {
            return await ProcessRequest(request.RequestUri.ParseQueryString());
        }

        [Route]
        public async Task<IHttpActionResult> Post(HttpRequestMessage request)
        {
            return await ProcessRequest(await request.Content.ReadAsFormDataAsync());
        }

        protected virtual async Task<IHttpActionResult> ProcessRequest(NameValueCollection parameters)
        {
            _logger.Start("OIDC authorize endpoint.");
            
            var signin = new SignInMessage();
            
            ///////////////////////////////////////////////////////////////
            // validate protocol parameters
            //////////////////////////////////////////////////////////////
            var result = _validator.ValidateProtocol(parameters);

            var request = _validator.ValidatedRequest;

            if (result.IsError)
            {
                return this.AuthorizeError(
                    result.ErrorType,
                    result.Error,
                    request.ResponseMode,
                    request.RedirectUri,
                    request.State);
            }

            var interaction = _interactionGenerator.ProcessLogin(request, User as ClaimsPrincipal);

            if (interaction.IsError)
            {
                return this.AuthorizeError(interaction.Error);
            }
            if (interaction.IsLogin)
            {
                return this.Login(interaction.SignInMessage, _settings);
            }

            ///////////////////////////////////////////////////////////////
            // validate client
            //////////////////////////////////////////////////////////////
            result = _validator.ValidateClient();

            if (result.IsError)
            {
                return this.AuthorizeError(
                    result.ErrorType,
                    result.Error,
                    request.ResponseMode,
                    request.RedirectUri,
                    request.State);
            }

            interaction = _interactionGenerator.ProcessConsent(request, User as ClaimsPrincipal);
            if (interaction.IsConsent)
            {
                // show consent page
            }

            return CreateAuthorizeResponse(request);
        }

        private IHttpActionResult CreateAuthorizeResponse(ValidatedAuthorizeRequest request)
        {
            if (request.Flow == Flows.Implicit)
            {
                return CreateImplicitFlowAuthorizeResponse(request);
            }

            if (request.Flow == Flows.Code)
            {
                return CreateCodeFlowAuthorizeResponse(request);
            }

            _logger.Error("Unsupported flow. Aborting.");
            throw new InvalidOperationException("Unsupported flow");
        }

        private IHttpActionResult CreateCodeFlowAuthorizeResponse(ValidatedAuthorizeRequest request)
        {
            var response = _responseGenerator.CreateCodeFlowResponse(request, User as ClaimsPrincipal);
            return this.AuthorizeCodeResponse(response);
        }

        private IHttpActionResult CreateImplicitFlowAuthorizeResponse(ValidatedAuthorizeRequest request)
        {
            var response = _responseGenerator.CreateImplicitFlowResponse(request, User as ClaimsPrincipal);

            // create form post response if responseMode is set form_post
            if (request.ResponseMode == Constants.ResponseModes.FormPost)
            {
                return this.AuthorizeImplicitFormPostResponse(response);
            }

            return this.AuthorizeImplicitFragmentResponse(response);
        }
    }
}