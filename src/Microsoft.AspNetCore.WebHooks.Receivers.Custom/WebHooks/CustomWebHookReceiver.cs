﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.AspNetCore.WebHooks.Receivers
{
    /// <summary>
    /// Provides an <see cref="IWebHookReceiver"/> implementation that can be used to receive WebHooks from parties 
    /// supporting WebHooks generated by the ASP.NET Custom WebHooks module. 
    /// Set the '<c>MS_WebHookReceiverSecret_Custom</c>' application setting to the application secrets, optionally using IDs
    /// to differentiate between multiple WebHooks, for example '<c>secret0, id1=secret1,id2=secret2</c>'.
    /// The corresponding WebHook URI is of the form '<c>https://&lt;host&gt;/api/webhooks/incoming/custom/{id}</c>'.
    /// </summary>
    public class CustomWebHookReceiver : WebHookReceiver
    {
        internal const string RecName = "custom";
        internal const int SecretMinLength = 32;
        internal const int SecretMaxLength = 128;

        internal const string EchoParameter = "echo";
        internal const string SignatureHeaderKey = "sha256";
        internal const string SignatureHeaderValueTemplate = SignatureHeaderKey + "={0}";
        internal const string SignatureHeaderName = "ms-signature";

        internal const string NotificationsKey = "Notifications";
        internal const string ActionKey = "Action";

        private readonly ILogger _logger;
        private readonly CustomWebHookReceiverOptions _options;

        public CustomWebHookReceiver(ILogger<CustomWebHookReceiver> logger, IOptions<CustomWebHookReceiverOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        /// <summary>
        /// Gets the receiver name for this receiver.
        /// </summary>
        public static string ReceiverName
        {
            get { return RecName; }
        }

        /// <inheritdoc />
        public override string Name
        {
            get { return RecName; }
        }

        /// <inheritdoc />
        public override async Task<WebHookHandlerContext> ReceiveAsync(PathString id, HttpContext context)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            if (context.Request.Method == "POST")
            {
                bool isValid = await VerifySignature(id, context);

                if (!isValid)
                {
                    // Return Nothing so no Handlers are fired
                    return null;
                }

                // Read the request entity body
                JObject data = await ReadBodyAsJsonAsync<JObject>(context.Request);

                // Get the event actions
                IEnumerable<string> actions = GetActions(data);

                // Build and Return the Context
                WebHookHandlerContext handlerContext = new WebHookHandlerContext(actions);
                handlerContext.Data = data;
                handlerContext.Id = id.Value;
                return handlerContext;
            }
            else if (context.Request.Method == "GET")
            {
                await WebHookVerification(id, context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid WebHook Request");
            }

            // Return Nothing so no Handlers are fired
            return null;
        }

        /// <summary>
        /// Verifies that the signature header matches that of the actual body.
        /// </summary>
        protected virtual async Task<bool> VerifySignature(string id, HttpContext context)
        {
            string secretKey = _options.GetReceiverConfig(Name, id);

            // Get the expected hash from the signature header
            string header = context.Request.Headers[SignatureHeaderName];
            string[] values = header.Split('=');
            if (values.Length != 2 || !string.Equals(values[0], SignatureHeaderKey, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid Signature");
                return false;
            }

            byte[] expectedHash;
            try
            {
                expectedHash = EncodingUtilities.FromHex(values[1]);
            }
            catch (Exception)
            {
                //string msg = string.Format(CultureInfo.CurrentCulture, CustomReceiverResources.Receiver_BadHeaderEncoding, SignatureHeaderName);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Bad Encoding");
                return false;
            }

            // Compute the actual hash of the request body
            byte[] actualHash;
            byte[] secret = Encoding.UTF8.GetBytes(secretKey);
            using (var hasher = new HMACSHA256(secret))
            {
                byte[] data = await ReadAsByteArrayAsync(context.Request);
                actualHash = hasher.ComputeHash(data);
            }

            // Now verify that the actual hash matches the expected hash.
            if (!WebHookReceiver.SecretEqual(expectedHash, actualHash))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Bad Signature");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Creates a response to a WebHook verification GET request.
        /// </summary>
        protected virtual async Task WebHookVerification(string id, HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            // Verify that we have the secret as an app setting
            string secret = _options.GetReceiverConfig(Name, id);
            if (String.IsNullOrWhiteSpace(secret))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Invalid Request");
                _logger.LogError("WebHook Verfication Failed - No Secret Found");
            }

            // Get the 'echo' parameter and echo it back to caller
            string echo = context.Request.Query[EchoParameter];
            if (string.IsNullOrEmpty(echo))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("No Echo Provided");
                _logger.LogError("WebHook Verfication Failed - No Echo Provided");
            }
            else
            {
                // Return the echo response
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(echo);
                _logger.LogTrace("WebHook Verfication Succeeded");
            }
        }

        /// <summary>
        /// Gets the notification actions form the given <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The request body.</param>
        /// <returns>A collection of actions.</returns>
        protected virtual IEnumerable<string> GetActions(JObject data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            List<string> actions = new List<string>();
            JArray notifications = data.Value<JArray>(NotificationsKey);
            if (notifications != null)
            {
                foreach (JObject e in notifications)
                {
                    string action = e.Value<string>(ActionKey);
                    if (action != null)
                    {
                        actions.Add(action);
                    }
                }
            }
            return actions;
        }
    }
}
