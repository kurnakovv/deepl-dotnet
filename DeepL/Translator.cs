﻿// Copyright 2022 DeepL SE (https://www.deepl.com)
// Use of this source code is governed by an MIT
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DeepL.Internal;
using DeepL.Model;

namespace DeepL {
  /// <summary>
  ///   Client for the DeepL API. To use the DeepL API, initialize an instance of this class using your DeepL
  ///   Authentication Key. All functions are thread-safe, aside from <see cref="Translator.Dispose" />.
  /// </summary>
  public sealed class Translator : IDisposable {
    /// <summary>Base URL for DeepL API Pro accounts.</summary>
    private const string DeepLServerUrl = "https://api.deepl.com";

    /// <summary>Base URL for DeepL API Free accounts.</summary>
    private const string DeepLServerUrlFree = "https://api-free.deepl.com";

    /// <summary>Internal class implementing HTTP requests.</summary>
    private readonly DeepLClient _client;

    /// <summary>Initializes a new <see cref="Translator" /> object using your authentication key.</summary>
    /// <param name="authKey">
    ///   Authentication Key as found in your
    ///   <a href="https://www.deepl.com/pro-account/">DeepL API account</a>.
    /// </param>
    /// <param name="options">Additional options controlling Translator behaviour.</param>
    /// <exception cref="ArgumentNullException">If authKey argument is null.</exception>
    /// <exception cref="ArgumentException">If authKey argument is empty.</exception>
    /// <remarks>
    ///   This function does not establish a connection to the DeepL API. To check connectivity, use
    ///   <see cref="GetUsageAsync" />.
    /// </remarks>
    public Translator(string authKey, TranslatorOptions? options = null) {
      options ??= new TranslatorOptions();

      if (authKey == null) {
        throw new ArgumentNullException(nameof(authKey));
      }

      authKey = authKey.Trim();

      if (authKey.Length == 0) {
        throw new ArgumentException($"{nameof(authKey)} is empty");
      }

      var serverUrl = new Uri(
            options.ServerUrl ?? (AuthKeyIsFreeAccount(authKey) ? DeepLServerUrlFree : DeepLServerUrl));

      var headers = new Dictionary<string, string?>(options.Headers, StringComparer.OrdinalIgnoreCase);

      if (!headers.ContainsKey("User-Agent")) {
        headers.Add("User-Agent", $"deepl-dotnet/{Version()}");
      }

      if (!headers.ContainsKey("Authorization")) {
        headers.Add("Authorization", $"DeepL-Auth-Key {authKey}");
      }

      var clientFactory = options.ClientFactory ?? (() =>
            DeepLClient.CreateDefaultHttpClient(
                  options.PerRetryConnectionTimeout,
                  options.OverallConnectionTimeout,
                  options.MaximumNetworkRetries));

      _client = new DeepLClient(
            serverUrl,
            clientFactory,
            headers);
    }

    /// <summary>Releases the unmanaged resources and disposes of the managed resources used by the <see cref="Translator" />.</summary>
    public void Dispose() => _client.Dispose();

    /// <summary>Retrieves the version string, with format MAJOR.MINOR.BUGFIX.</summary>
    /// <returns>String containing the library version.</returns>
    public static string Version() {
      var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";
      return version;
    }

    /// <summary>
    ///   Determines if the given DeepL Authentication Key belongs to an API Free account or an API Pro account.
    /// </summary>
    /// <param name="authKey">
    ///   DeepL Authentication Key as found in your
    ///   <a href="https://www.deepl.com/pro-account/">DeepL API account</a>.
    /// </param>
    /// <returns>
    ///   <c>true</c> if the Authentication Key belongs to an API Free account, <c>false</c> if it belongs to an API Pro
    ///   account.
    /// </returns>
    public static bool AuthKeyIsFreeAccount(string authKey) => authKey.TrimEnd().EndsWith(":fx");

    /// <summary>Retrieves the usage in the current billing period for this DeepL account.</summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="Usage" /> object containing account usage information.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<Usage> GetUsageAsync(CancellationToken cancellationToken = default) {
      using var responseMessage = await _client.ApiGetAsync("/v2/usage", cancellationToken).ConfigureAwait(false);
      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      var usageFields = await JsonUtils.DeserializeAsync<Usage.JsonFieldsStruct>(responseMessage)
            .ConfigureAwait(false);
      return new Usage(usageFields);
    }

    /// <summary>Translate specified texts from source language into target language.</summary>
    /// <param name="texts">Texts to translate; must not be empty.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options">Extra <see cref="TextTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Texts translated into specified target language.</returns>
    /// <exception cref="ArgumentException">If any argument is invalid.</exception>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<TextResult[]> TranslateTextAsync(
          IEnumerable<string> texts,
          string? sourceLanguageCode,
          string targetLanguageCode,
          TextTranslateOptions? options = null,
          CancellationToken cancellationToken = default) {
      var bodyParams = CreateHttpParams(sourceLanguageCode, targetLanguageCode, options);
      var textParams = texts
            .Where(text => text.Length > 0 ? true : throw new ArgumentException("text must not be empty"))
            .Select(text => ("text", text));

      using var responseMessage = await _client
            .ApiPostAsync("/v2/translate", cancellationToken, bodyParams.Concat(textParams)).ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      var translatedTexts =
            await JsonUtils.DeserializeAsync<TextTranslateResult>(responseMessage).ConfigureAwait(false);
      return translatedTexts.Translations;
    }

    /// <summary>Translate specified text from source language into target language.</summary>
    /// <param name="text">Text to translate; must not be empty.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options"><see cref="TextTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Text translated into specified target language.</returns>
    /// <exception cref="ArgumentException">If any argument is invalid.</exception>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<TextResult> TranslateTextAsync(
          string text,
          string? sourceLanguageCode,
          string targetLanguageCode,
          TextTranslateOptions? options = null,
          CancellationToken cancellationToken = default)
      => (await TranslateTextAsync(
                  new[] { text },
                  sourceLanguageCode,
                  targetLanguageCode,
                  options,
                  cancellationToken)
            .ConfigureAwait(false))[0];

    /// <summary>
    ///   Translate document at specified input <see cref="FileInfo" /> from source language to target language and
    ///   store the translated document at specified output <see cref="FileInfo" />.
    /// </summary>
    /// <param name="inputFileInfo"><see cref="FileInfo" /> object containing path to input document.</param>
    /// <param name="outputFileInfo"><see cref="FileInfo" /> object containing path to store translated document.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options"><see cref="DocumentTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="IOException">If the output path is occupied.</exception>
    /// <exception cref="DocumentTranslationException">
    ///   If cancellation was requested, or an error occurs while translating the document. If the document was uploaded
    ///   successfully, then the <see cref="DocumentTranslationException.DocumentHandle" /> contains the document ID and
    ///   key that may be used to retrieve the document.
    ///   If cancellation was requested, the <see cref="DocumentTranslationException.InnerException" /> will be a
    ///   <see cref="TaskCanceledException" />.
    /// </exception>
    public async Task TranslateDocumentAsync(
          FileInfo inputFileInfo,
          FileInfo outputFileInfo,
          string? sourceLanguageCode,
          string targetLanguageCode,
          DocumentTranslateOptions? options = null,
          CancellationToken cancellationToken = default) {
      using var inputFile = inputFileInfo.OpenRead();
      using var outputFile = outputFileInfo.Open(FileMode.CreateNew, FileAccess.Write);
      try {
        await TranslateDocumentAsync(
              inputFile,
              inputFileInfo.Name,
              outputFile,
              sourceLanguageCode,
              targetLanguageCode,
              options,
              cancellationToken).ConfigureAwait(false);
      } catch {
        try {
          outputFileInfo.Delete();
        } catch {
          // ignored
        }

        throw;
      }
    }

    /// <summary>
    ///   Translate specified document content from source language to target language and store the translated document
    ///   content to specified stream.
    /// </summary>
    /// <param name="inputFile"><see cref="Stream" /> containing input document content.</param>
    /// <param name="inputFileName">Name of the input file. The file extension is used to determine file type.</param>
    /// <param name="outputFile"><see cref="Stream" /> to store translated document content.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options"><see cref="DocumentTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DocumentTranslationException">
    ///   If cancellation was requested, or an error occurs while translating the document. If the document was uploaded
    ///   successfully, then the <see cref="DocumentTranslationException.DocumentHandle" /> contains the document ID and
    ///   key that may be used to retrieve the document.
    ///   If cancellation was requested, the <see cref="DocumentTranslationException.InnerException" /> will be a
    ///   <see cref="TaskCanceledException" />.
    /// </exception>
    public async Task TranslateDocumentAsync(
          Stream inputFile,
          string inputFileName,
          Stream outputFile,
          string? sourceLanguageCode,
          string targetLanguageCode,
          DocumentTranslateOptions? options = null,
          CancellationToken cancellationToken = default) {
      DocumentHandle? handle = null;
      try {
        handle = await TranslateDocumentUploadAsync(
                    inputFile,
                    inputFileName,
                    sourceLanguageCode,
                    targetLanguageCode,
                    options,
                    cancellationToken)
              .ConfigureAwait(false);
        await TranslateDocumentWaitUntilDoneAsync(handle.Value, cancellationToken).ConfigureAwait(false);
        await TranslateDocumentDownloadAsync(handle.Value, outputFile, cancellationToken).ConfigureAwait(false);
      } catch (Exception exception) {
        throw new DocumentTranslationException(
              $"Error occurred during document translation: {exception.Message}",
              exception,
              handle);
      }
    }

    /// <summary>
    ///   Upload document at specified input <see cref="FileInfo" /> for translation from source language to target
    ///   language.
    /// </summary>
    /// <param name="inputFileInfo"><see cref="FileInfo" /> object containing path to input document.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options"><see cref="DocumentTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="DocumentHandle" /> object associated with the in-progress document translation.</returns>
    /// <exception cref="ArgumentException">If any argument is invalid.</exception>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<DocumentHandle> TranslateDocumentUploadAsync(
          FileInfo inputFileInfo,
          string? sourceLanguageCode,
          string targetLanguageCode,
          DocumentTranslateOptions? options = null,
          CancellationToken cancellationToken = default) {
      using var inputFileStream = inputFileInfo.OpenRead();
      return await TranslateDocumentUploadAsync(
            inputFileStream,
            inputFileInfo.Name,
            sourceLanguageCode,
            targetLanguageCode,
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upload document content with specified filename for translation from source language to target language.</summary>
    /// <param name="inputFile"><see cref="Stream" /> containing input document content.</param>
    /// <param name="inputFileName">Name of the input file. The file extension is used to determine file type.</param>
    /// <param name="sourceLanguageCode">Language code of the input language, or null to use auto-detection.</param>
    /// <param name="targetLanguageCode">Language code of the desired output language.</param>
    /// <param name="options"><see cref="DocumentTranslateOptions" /> influencing translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="DocumentHandle" /> object associated with the in-progress document translation.</returns>
    /// <exception cref="ArgumentException">If any argument is invalid.</exception>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<DocumentHandle> TranslateDocumentUploadAsync(
          Stream inputFile,
          string inputFileName,
          string? sourceLanguageCode,
          string targetLanguageCode,
          DocumentTranslateOptions? options = null,
          CancellationToken cancellationToken = default) {
      var bodyParams = CreateHttpParams(
            sourceLanguageCode,
            targetLanguageCode,
            options);

      using var responseMessage = await _client.ApiUploadAsync(
                  "/v2/document/",
                  cancellationToken,
                  bodyParams,
                  inputFile,
                  inputFileName)
            .ConfigureAwait(false);
      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      return await JsonUtils.DeserializeAsync<DocumentHandle>(responseMessage).ConfigureAwait(false);
    }

    /// <summary>
    ///   Retrieve the status of in-progress document translation associated with specified
    ///   <see cref="DocumentHandle" />.
    /// </summary>
    /// <param name="handle"><see cref="DocumentHandle" /> associated with an in-progress document translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="DocumentStatus" /> object containing the document translation status.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<DocumentStatus> TranslateDocumentStatusAsync(
          DocumentHandle handle,
          CancellationToken cancellationToken = default) {
      var bodyParams = new (string Key, string Value)[] { ("document_key", handle.DocumentKey) };
      using var responseMessage =
            await _client.ApiPostAsync(
                        $"/v2/document/{handle.DocumentId}",
                        cancellationToken,
                        bodyParams)
                  .ConfigureAwait(false);
      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      return await JsonUtils.DeserializeAsync<DocumentStatus>(responseMessage).ConfigureAwait(false);
    }

    /// <summary>
    ///   Checks document translation status and asynchronously-waits until document translation is complete or fails
    ///   due to an error.
    /// </summary>
    /// <param name="handle"><see cref="DocumentHandle" /> associated with an in-progress document translation.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task TranslateDocumentWaitUntilDoneAsync(
          DocumentHandle handle,
          CancellationToken cancellationToken = default) {
      var status = await TranslateDocumentStatusAsync(handle, cancellationToken).ConfigureAwait(false);
      while (status.Ok && !status.Done) {
        await Task.Delay(CalculateDocumentWaitTime(status.SecondsRemaining), cancellationToken).ConfigureAwait(false);
        status = await TranslateDocumentStatusAsync(handle, cancellationToken).ConfigureAwait(false);
      }

      if (!status.Ok) {
        throw new DeepLException(status.ErrorMessage ?? "Unknown error");
      }
    }

    /// <summary>
    ///   Downloads the resulting translated document associated with specified <see cref="DocumentHandle" /> to the
    ///   specified <see cref="FileInfo" />. The <see cref="DocumentStatus" /> for the document must have
    ///   <see cref="DocumentStatus.Done" /> == <c>true</c>.
    /// </summary>
    /// <param name="handle"><see cref="DocumentHandle" /> associated with an in-progress document translation.</param>
    /// <param name="outputFileInfo"><see cref="FileInfo" /> object containing path to store translated document.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="IOException">If the output path is occupied.</exception>
    /// <exception cref="DocumentNotReadyException">
    ///   If the document is not ready to be downloaded, that is, the document status
    ///   is not <see cref="DocumentStatus.Done" />.
    /// </exception>
    /// <exception cref="DeepLException">
    ///   If any other error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task TranslateDocumentDownloadAsync(
          DocumentHandle handle,
          FileInfo outputFileInfo,
          CancellationToken cancellationToken = default) {
      using var outputFileStream = outputFileInfo.Open(FileMode.CreateNew, FileAccess.Write);
      try {
        await TranslateDocumentDownloadAsync(handle, outputFileStream, cancellationToken).ConfigureAwait(false);
      } catch {
        try {
          outputFileInfo.Delete();
        } catch {
          // ignored
        }

        throw;
      }
    }

    /// <summary>
    ///   Downloads the resulting translated document associated with specified <see cref="DocumentHandle" /> to the
    ///   specified <see cref="Stream" />. The <see cref="DocumentStatus" /> for the document must have
    ///   <see cref="DocumentStatus.Done" /> == <c>true</c>.
    /// </summary>
    /// <param name="handle"><see cref="DocumentHandle" /> associated with an in-progress document translation.</param>
    /// <param name="outputFile"><see cref="Stream" /> to store translated document content.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DocumentNotReadyException">
    ///   If the document is not ready to be downloaded, that is, the document status
    ///   is not <see cref="DocumentStatus.Done" />.
    /// </exception>
    /// <exception cref="DeepLException">
    ///   If any other error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task TranslateDocumentDownloadAsync(
          DocumentHandle handle,
          Stream outputFile,
          CancellationToken cancellationToken = default) {
      var bodyParams = new (string Key, string Value)[] { ("document_key", handle.DocumentKey) };
      using var responseMessage = await _client.ApiPostAsync(
                  $"/v2/document/{handle.DocumentId}/result",
                  cancellationToken,
                  bodyParams)
            .ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, downloadingDocument: true).ConfigureAwait(false);
      await responseMessage.Content.CopyToAsync(outputFile).ConfigureAwait(false);
    }

    /// <summary>Retrieves the list of supported translation source languages.</summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Array of <see cref="Language" /> objects representing the available translation source languages.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<SourceLanguage[]> GetSourceLanguagesAsync(CancellationToken cancellationToken = default) =>
          await GetLanguagesAsync<SourceLanguage>(false, cancellationToken).ConfigureAwait(false);

    /// <summary>Retrieves the list of supported translation target languages.</summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Array of <see cref="Language" /> objects representing the available translation target languages.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<TargetLanguage[]> GetTargetLanguagesAsync(CancellationToken cancellationToken = default) =>
          await GetLanguagesAsync<TargetLanguage>(true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///   Retrieves the list of supported glossary language pairs. When creating glossaries, the source and target
    ///   language pair must match one of the available language pairs.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Array of <see cref="GlossaryLanguagePair" /> objects representing the available glossary language pairs.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryLanguagePair[]> GetGlossaryLanguagesAsync(CancellationToken cancellationToken = default) {
      using var responseMessage = await _client
            .ApiGetAsync("/v2/glossary-language-pairs", cancellationToken).ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      var languages = await JsonUtils.DeserializeAsync<GlossaryLanguageListResult>(responseMessage)
            .ConfigureAwait(false);
      return languages.GlossaryLanguagePairs;
    }

    /// <summary>
    ///   Creates a glossary in your DeepL account with the specified details and returns a <see cref="GlossaryInfo" />
    ///   object with details about the newly created glossary. The glossary can be used in translations to override
    ///   translations for specific terms (words). The glossary source and target languages must match the languages of
    ///   translations for which it will be used.
    /// </summary>
    /// <param name="name">User-defined name to assign to the glossary; must not be empty.</param>
    /// <param name="sourceLanguageCode">Language code of the source terms language.</param>
    /// <param name="targetLanguageCode">Language code of the target terms language.</param>
    /// <param name="entries">Glossary entry list.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="GlossaryInfo" /> object with details about the newly created glossary.</returns>
    /// <exception cref="ArgumentException">If any argument is invalid.</exception>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryInfo> CreateGlossaryAsync(
          string name,
          string sourceLanguageCode,
          string targetLanguageCode,
          GlossaryEntries entries,
          CancellationToken cancellationToken = default) {
      if (name.Length == 0) {
        throw new ArgumentException($"Parameter {nameof(name)} must not be empty");
      }

      sourceLanguageCode = LanguageCode.RemoveRegionalVariant(sourceLanguageCode);
      targetLanguageCode = LanguageCode.RemoveRegionalVariant(targetLanguageCode);

      var entriesTsv = entries.ToTsv();

      var bodyParams = new (string Key, string Value)[] {
            ("name", name), ("source_lang", sourceLanguageCode), ("target_lang", targetLanguageCode),
            ("entries_format", "tsv"), ("entries", entriesTsv)
      };
      using var responseMessage =
            await _client.ApiPostAsync("/v2/glossaries", cancellationToken, bodyParams).ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, true).ConfigureAwait(false);
      return await JsonUtils.DeserializeAsync<GlossaryInfo>(responseMessage).ConfigureAwait(false);
    }

    /// <summary>
    ///   Retrieves information about the glossary with the specified ID and returns a <see cref="GlossaryInfo" />
    ///   object containing details. This does not retrieve the glossary entries; to retrieve entries use
    ///   <see cref="GetGlossaryEntriesAsync(string, CancellationToken)" />
    /// </summary>
    /// <param name="glossaryId">ID of glossary to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="GlossaryInfo" /> object with details about the specified glossary.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryInfo> GetGlossaryAsync(
          string glossaryId,
          CancellationToken cancellationToken = default) {
      using var responseMessage =
            await _client.ApiGetAsync($"/v2/glossaries/{glossaryId}", cancellationToken)
                  .ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, true).ConfigureAwait(false);
      return await JsonUtils.DeserializeAsync<GlossaryInfo>(responseMessage).ConfigureAwait(false);
    }

    /// <summary>Asynchronously waits until the given glossary is ready to be used for translations.</summary>
    /// <param name="glossaryId">ID of glossary to ensure ready.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryInfo> WaitUntilGlossaryReadyAsync(
          string glossaryId,
          CancellationToken cancellationToken = default) {
      var info = await GetGlossaryAsync(glossaryId, cancellationToken);
      while (!info.Ready) {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        info = await GetGlossaryAsync(glossaryId, cancellationToken);
      }

      return info;
    }

    /// <summary>
    ///   Retrieves information about all glossaries and returns an array of <see cref="GlossaryInfo" /> objects
    ///   containing details. This does not retrieve the glossary entries; to retrieve entries use
    ///   <see cref="GetGlossaryEntriesAsync(string, CancellationToken)" />
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Array of <see cref="GlossaryInfo" /> objects with details about each glossary.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryInfo[]> ListGlossariesAsync(CancellationToken cancellationToken = default) {
      using var responseMessage =
            await _client.ApiGetAsync("/v2/glossaries", cancellationToken).ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, true).ConfigureAwait(false);
      return (await JsonUtils.DeserializeAsync<GlossaryListResult>(responseMessage).ConfigureAwait(false)).Glossaries;
    }

    /// <summary>
    ///   Retrieves the entries containing within the glossary with the specified ID and returns them as a
    ///   <see cref="GlossaryEntries" />.
    /// </summary>
    /// <param name="glossaryId">ID of glossary for which to retrieve entries.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="GlossaryEntries" /> containing entry pairs of the glossary.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryEntries> GetGlossaryEntriesAsync(
          string glossaryId,
          CancellationToken cancellationToken = default) {
      using var responseMessage = await _client.ApiGetAsync(
            $"/v2/glossaries/{glossaryId}/entries",
            cancellationToken,
            acceptHeader: "text/tab-separated-values").ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, true).ConfigureAwait(false);
      var contentTsv = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
      return GlossaryEntries.FromTsv(contentTsv, true);
    }

    /// <summary>
    ///   Retrieves the entries containing within the glossary and returns them as a <see cref="GlossaryEntries" />.
    /// </summary>
    /// <param name="glossary"><see cref="GlossaryInfo" /> object corresponding to glossary for which to retrieve entries.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns><see cref="GlossaryEntries" /> containing entry pairs of the glossary.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task<GlossaryEntries> GetGlossaryEntriesAsync(
          GlossaryInfo glossary,
          CancellationToken cancellationToken = default) =>
          await GetGlossaryEntriesAsync(glossary.GlossaryId, cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the glossary with the specified ID.</summary>
    /// <param name="glossaryId">ID of glossary to delete.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task DeleteGlossaryAsync(
          string glossaryId,
          CancellationToken cancellationToken = default) {
      using var responseMessage =
            await _client.ApiDeleteAsync($"/v2/glossaries/{glossaryId}", cancellationToken)
                  .ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage, true).ConfigureAwait(false);
    }

    /// <summary>Deletes the specified glossary.</summary>
    /// <param name="glossary"><see cref="GlossaryInfo" /> object corresponding to glossary to delete.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    public async Task DeleteGlossaryAsync(
          GlossaryInfo glossary,
          CancellationToken cancellationToken = default) =>
          await DeleteGlossaryAsync(glossary.GlossaryId, cancellationToken).ConfigureAwait(false);

    /// <summary>Internal function to retrieve available languages.</summary>
    /// <param name="target"><c>true</c> to retrieve target languages, <c>false</c> to retrieve source languages.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Array of <see cref="Language" /> objects containing information about the available languages.</returns>
    /// <exception cref="DeepLException">
    ///   If any error occurs while communicating with the DeepL API, a
    ///   <see cref="DeepLException" /> or a derived class will be thrown.
    /// </exception>
    private async Task<TValue[]> GetLanguagesAsync<TValue>(
          bool target,
          CancellationToken cancellationToken = default) {
      var queryParams = new (string Key, string Value)[] { ("type", target ? "target" : "source") };
      using var responseMessage =
            await _client.ApiGetAsync("/v2/languages", cancellationToken, queryParams)
                  .ConfigureAwait(false);

      await DeepLClient.CheckStatusCodeAsync(responseMessage).ConfigureAwait(false);
      return await JsonUtils.DeserializeAsync<TValue[]>(responseMessage).ConfigureAwait(false);
    }

    /// <summary>
    ///   Checks the specified languages and options are valid, and returns an enumerable of tuples containing the parameters
    ///   to include in HTTP request.
    /// </summary>
    /// <param name="sourceLanguageCode">
    ///   Language code of translation source language, or null if auto-detection should be
    ///   used.
    /// </param>
    /// <param name="targetLanguageCode">Language code of translation target language.</param>
    /// <param name="options">Extra <see cref="TextTranslateOptions" /> influencing translation.</param>
    /// <returns>Enumerable of tuples containing the parameters to include in HTTP request.</returns>
    /// <exception cref="ArgumentException">If the specified languages or options are invalid.</exception>
    private IEnumerable<(string Key, string Value)> CreateHttpParams(
          string? sourceLanguageCode,
          string targetLanguageCode,
          TextTranslateOptions? options) {
      targetLanguageCode = LanguageCode.Standardize(targetLanguageCode);
      sourceLanguageCode = sourceLanguageCode == null ? null : LanguageCode.Standardize(sourceLanguageCode);

      CheckValidLanguages(sourceLanguageCode, targetLanguageCode);

      var bodyParams = new List<(string Key, string Value)> { ("target_lang", targetLanguageCode) };
      if (sourceLanguageCode != null) {
        bodyParams.Add(("source_lang", sourceLanguageCode));
      }

      if (options == null) {
        return bodyParams;
      }

      if (options.GlossaryId != null) {
        if (sourceLanguageCode == null) {
          throw new ArgumentException($"{nameof(sourceLanguageCode)} is required if using a glossary");
        }

        bodyParams.Add(("glossary_id", options.GlossaryId));
      }

      if (options.Formality != Formality.Default) {
        bodyParams.Add(("formality", options.Formality.ToString().ToLowerInvariant()));
      }

      if (options.SentenceSplittingMode != SentenceSplittingMode.All) {
        bodyParams.Add(
              ("split_sentences", options.SentenceSplittingMode == SentenceSplittingMode.Off ? "0" : "nonewlines"));
      }

      if (options.PreserveFormatting) {
        bodyParams.Add(("preserve_formatting", "1"));
      }

      if (options.TagHandling != null) {
        bodyParams.Add(("tag_handling", options.TagHandling));
      }

      if (options.OutlineDetection) {
        bodyParams.Add(("outline_detection", "0"));
      }

      if (options.NonSplittingTags.Count > 0) {
        bodyParams.Add(("non_splitting_tags", string.Join(",", options.NonSplittingTags)));
      }

      if (options.SplittingTags.Count > 0) {
        bodyParams.Add(("splitting_tags", string.Join(",", options.SplittingTags)));
      }

      if (options.IgnoreTags.Count > 0) {
        bodyParams.Add(("ignore_tags", string.Join(",", options.IgnoreTags)));
      }

      return bodyParams;
    }

    /// <summary>
    ///   Checks the specified languages and options are valid, and returns an enumerable of tuples containing the parameters
    ///   to include in HTTP request.
    /// </summary>
    /// <param name="sourceLanguageCode">
    ///   Language code of translation source language, or null if auto-detection should be
    ///   used.
    /// </param>
    /// <param name="targetLanguageCode">Language code of translation target language.</param>
    /// <param name="options">Extra <see cref="DocumentTranslateOptions" /> influencing translation.</param>
    /// <returns>Enumerable of tuples containing the parameters to include in HTTP request.</returns>
    /// <exception cref="ArgumentException">If the specified languages or options are invalid.</exception>
    private IEnumerable<(string Key, string Value)> CreateHttpParams(
          string? sourceLanguageCode,
          string targetLanguageCode,
          DocumentTranslateOptions? options) {
      targetLanguageCode = LanguageCode.Standardize(targetLanguageCode);
      sourceLanguageCode = sourceLanguageCode == null ? null : LanguageCode.Standardize(sourceLanguageCode);

      CheckValidLanguages(sourceLanguageCode, targetLanguageCode);

      var bodyParams = new List<(string Key, string Value)> { ("target_lang", targetLanguageCode) };
      if (sourceLanguageCode != null) {
        bodyParams.Add(("source_lang", sourceLanguageCode));
      }

      if (options == null) {
        return bodyParams;
      }

      if (options.GlossaryId != null) {
        if (sourceLanguageCode == null) {
          throw new ArgumentException($"{nameof(sourceLanguageCode)} is required if using a glossary");
        }

        bodyParams.Add(("glossary_id", options.GlossaryId));
      }

      if (options.Formality != Formality.Default) {
        bodyParams.Add(("formality", options.Formality.ToString().ToLowerInvariant()));
      }

      return bodyParams;
    }

    /// <summary>Checks the specified source and target language are valid, and throws an exception if not.</summary>
    /// <param name="sourceLanguageCode">Language code of translation source language, or null if auto-detection is used.</param>
    /// <param name="targetLanguageCode">Language code of translation target language.</param>
    /// <exception cref="ArgumentException">If source or target language code are not valid.</exception>
    private static void CheckValidLanguages(string? sourceLanguageCode, string targetLanguageCode) {
      if (sourceLanguageCode is { Length: 0 }) {
        throw new ArgumentException($"{nameof(sourceLanguageCode)} must not be empty");
      }

      if (targetLanguageCode.Length == 0) {
        throw new ArgumentException($"{nameof(targetLanguageCode)} must not be empty");
      }

      switch (targetLanguageCode) {
        case "en":
          throw new ArgumentException(
                $"{nameof(targetLanguageCode)}=\"en\" is deprecated, please use \"en-GB\" or \"en-US\" instead");
        case "pt":
          throw new ArgumentException(
                $"{nameof(targetLanguageCode)}=\"pt\" is deprecated, please use \"pt-PT\" or \"pt-BR\" instead");
      }
    }

    /// <summary>
    ///   Determines recommended time to wait before checking document translation again, using an optional hint of
    ///   seconds remaining.
    /// </summary>
    /// <param name="hintSecondsRemaining">Optional hint of the number of seconds remaining.</param>
    /// <returns><see cref="TimeSpan" /> to wait.</returns>
    private static TimeSpan CalculateDocumentWaitTime(int? hintSecondsRemaining) {
      var secs = ((hintSecondsRemaining ?? 0) / 2.0) + 1.0;
      secs = Math.Max(1.0, Math.Min(secs, 60.0));
      return TimeSpan.FromSeconds(secs);
    }

    /// <summary>Class used for JSON-deserialization of text translate results.</summary>
    private readonly struct TextTranslateResult {
      /// <summary>Initializes a new instance of <see cref="TextTranslateResult" />, used for JSON deserialization.</summary>
      [JsonConstructor]
      public TextTranslateResult(TextResult[] translations) {
        Translations = translations;
      }

      /// <summary>Array of <see cref="TextResult" /> objects holding text translation results.</summary>
      public TextResult[] Translations { get; }
    }

    /// <summary>Class used for JSON-deserialization of glossary list results.</summary>
    private readonly struct GlossaryListResult {
      /// <summary>Initializes a new instance of <see cref="GlossaryListResult" />, used for JSON deserialization.</summary>
      [JsonConstructor]
      public GlossaryListResult(GlossaryInfo[] glossaries) {
        Glossaries = glossaries;
      }

      /// <summary>Array of <see cref="GlossaryInfo" /> objects holding glossary information.</summary>
      public GlossaryInfo[] Glossaries { get; }
    }

    /// <summary>Class used for JSON-deserialization of results of supported languages for glossaries.</summary>
    private readonly struct GlossaryLanguageListResult {
      /// <summary>Initializes a new instance of <see cref="GlossaryLanguageListResult" />, used for JSON deserialization.</summary>
      [JsonConstructor]
      public GlossaryLanguageListResult(GlossaryLanguagePair[] glossaryLanguagePairs) {
        GlossaryLanguagePairs = glossaryLanguagePairs;
      }

      /// <summary>Array of <see cref="GlossaryLanguagePair" /> objects holding supported glossary language pairs.</summary>
      [JsonPropertyName("supported_languages")]
      public GlossaryLanguagePair[] GlossaryLanguagePairs { get; }
    }
  }
}
