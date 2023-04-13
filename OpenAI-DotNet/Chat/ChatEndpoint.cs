using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAI.Chat
{
    /// <summary>
    /// Given a chat conversation, the model will return a chat completion response.<br/>
    /// <see href="https://platform.openai.com/docs/api-reference/chat"/>
    /// </summary>
    public sealed class ChatEndpoint : BaseEndPoint
    {
        /// <inheritdoc />
        public ChatEndpoint(OpenAIClient api) : base(api) { }

        /// <inheritdoc />
        protected override string Root => "chat";

        /// <summary>
        /// Creates a completion for the chat message
        /// </summary>
        /// <param name="chatRequest">The chat request which contains the message content.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="ChatResponse"/>.</returns>
        public async Task<ChatResponse> GetCompletionAsync(ChatRequest chatRequest, CancellationToken cancellationToken = default)
        {
            var jsonContent = JsonSerializer.Serialize(chatRequest, Api.JsonSerializationOptions).ToJsonStringContent();
            var httpResponse = await Api.Client.PostAsync(GetUrl("/completions"), jsonContent, cancellationToken).ConfigureAwait(false);
            var responseAsString = await httpResponse.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var response = httpResponse.DeserializeResponse<ChatResponse>(responseAsString, Api.JsonSerializationOptions);

            if (response == null)
                throw new InvalidDataException("This would never happen! Remote server returned a 'null' value.");

            return response;
        }


        /// <summary>
        /// Created a completion for the chat message and stream the results to the <paramref name="resultHandler"/> as they come in.
        /// </summary>
        /// <param name="chatRequest">The chat request which contains the message content.</param>
        /// <param name="resultHandler">An action to be called as each new result arrives.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="ChatResponse"/>.</returns>
        /// <exception cref="HttpRequestException">Raised when the HTTP request fails</exception>
        public async Task StreamCompletionAsync(ChatRequest chatRequest, Action<ChatResponse> resultHandler, CancellationToken cancellationToken = default)
        {
            chatRequest.Stream = true;
            var jsonContent = JsonSerializer.Serialize(chatRequest, Api.JsonSerializationOptions).ToJsonStringContent();
            using var request = new HttpRequestMessage(HttpMethod.Post, GetUrl("/completions"))
            {
                Content = jsonContent
            };
            var response = await Api.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await response.CheckResponseAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();

                if (line.StartsWith("data: "))
                {
                    line = line["data: ".Length..];
                }

                if (line == "[DONE]")
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    var responseSeg = response.DeserializeResponse<ChatResponse>(line.Trim(), Api.JsonSerializationOptions);

                    if (responseSeg != null)
                        resultHandler.Invoke(responseSeg);
                }
            }
        }

        /// <summary>
        /// Created a completion for the chat message and stream the results as they come in.<br/>
        /// If you are not using C# 8 supporting IAsyncEnumerable{T} or if you are using the .NET Framework,
        /// you may need to use <see cref="StreamCompletionAsync(ChatRequest, Action{ChatResponse}, CancellationToken)"/> instead.
        /// </summary>
        /// <param name="chatRequest">The chat request which contains the message content.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="ChatResponse"/>.</returns>
        /// <exception cref="HttpRequestException">Raised when the HTTP request fails</exception>
        public async IAsyncEnumerable<ChatResponse> StreamCompletionEnumerableAsync(ChatRequest chatRequest, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            chatRequest.Stream = true;
            var jsonContent = JsonSerializer.Serialize(chatRequest, Api.JsonSerializationOptions).ToJsonStringContent();
            using var request = new HttpRequestMessage(HttpMethod.Post, GetUrl("/completions"))
            {
                Content = jsonContent
            };
            var response = await Api.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await response.CheckResponseAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (line.StartsWith("data: "))
                {
                    line = line["data: ".Length..];
                }

                if (line == "[DONE]")
                {
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    var responseSeg = response.DeserializeResponse<ChatResponse>(line.Trim(), Api.JsonSerializationOptions);

                    if (responseSeg != null)
                        yield return responseSeg;
                }
            }
        }
    }
}