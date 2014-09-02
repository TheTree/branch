﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Web;
using Branch.Core.Game.HaloReach.Enums;
using Branch.Core.Game.HaloReach.Models._343;
using Branch.Core.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using HttpMethod = Branch.Core.Enums.HttpMethod;
using Branch.Core.Game.HaloReach.Models._343.Responces;

namespace Branch.Core.Game.HaloReach.Api
{
	public class Manager
	{
		private const string Game = "reach";
		private const string ApiKey = "waypoint";
		private const string ApiBase = "https://mobile-service-ssl.halo.xbox.com/{0}/{0}apijson.svc/{1}";
		private readonly AzureStorage _storage;

		public Manager(AzureStorage storage)
		{
			_storage = storage;

			GetMetadata();
		}

		public Metadata Metadata { get; private set; }

		#region Setup Manager

		/// <summary>
		/// 
		/// </summary>
		public void GetMetadata()
		{
			var metadataBlob = _storage.Blob.GetBlob(_storage.Blob.HReachBlobContainer,
				GenerateBlobContainerPath(BlobType.Other, "metadata"));
			var metadata = _storage.Blob.FindAndDownloadBlob<Metadata>(_storage.Blob.HReachBlobContainer,
				GenerateBlobContainerPath(BlobType.Other, "metadata"));

			if (metadataBlob == null || metadata == null ||
				metadataBlob.Properties.LastModified + TimeSpan.FromMinutes(14) < DateTime.UtcNow)
				UpdateMetadata(true);
			else
				Metadata = metadata;
		}

		#endregion

		#region Misc Endpoints

		/// <summary>
		/// 
		/// </summary>
		public void UpdateMetadata(bool useCached = false)
		{
			Metadata = UpdateOther<Metadata>("metadata", String.Format("game/metadata/{0}", ApiKey), useCached);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="blobFileName"></param>
		/// <param name="endpoint"></param>
		/// <param name="useCached"></param>
		/// <returns></returns>
		private T UpdateOther<T>(string blobFileName, string endpoint, bool useCached = false)
			where T : Response
		{
			var blobPath = GenerateBlobContainerPath(BlobType.Other, blobFileName);
			var otherData = _storage.Blob.FindAndDownloadBlob<T>(_storage.Blob.H4BlobContainer, blobPath);

			if (otherData != null && otherData.Reason != null && useCached) return otherData;
			var otherDataString = ValidateResponseAndGetRawText(UnauthorizedRequest(PopulateUrl(endpoint)));
			if (otherDataString == null) return null;

			// Save
			_storage.Blob.UploadBlob(_storage.Blob.HReachBlobContainer, GenerateBlobContainerPath(BlobType.Other, blobFileName),
				otherDataString);

			otherData = ParseText<T>(otherDataString);
			return otherData;
		}

		#endregion

		#region Unauthorized Request

		private static HttpResponseMessage UnauthorizedRequest(string endpoint)
		{
			// ReSharper disable once IntroduceOptionalParameters.Local
			return UnauthorizedRequest(endpoint, HttpMethod.Get);
		}

		private static HttpResponseMessage UnauthorizedRequest(string endpoint, HttpMethod requestType)
		{
			return UnauthorizedRequest(endpoint, requestType, new Dictionary<String, String>());
		}

		private static HttpResponseMessage UnauthorizedRequest(string endpoint, HttpMethod requestType, Dictionary<String, String> headers)
		{
			if (headers == null)
				headers = new Dictionary<string, string>();

			var httpClient = new HttpClient();
			foreach (var header in headers)
				httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

			var url = String.Format(ApiBase, Game, endpoint);
			switch (requestType)
			{
				case HttpMethod.Get:
					return httpClient.GetAsync(url).Result;

				default:
					throw new ArgumentException();
			}
		}

		#endregion

		#region Api Helpers

		/// <summary>
		///     Populates a url with the default params populated, and also populates custom params.
		/// </summary>
		/// <param name="endpoint">The url to populate.</param>
		/// <param name="queryParams"></param>
		/// <returns>A string representation of the populated url</returns>
		private static string PopulateUrl(string endpoint, Dictionary<string, string> queryParams = null)
		{
			if (queryParams == null)
				queryParams = new Dictionary<string, string>();

			var startOfParams = !endpoint.Contains("?");
			foreach (var queryParam in queryParams)
			{
				endpoint += startOfParams ? "?" : "&";
				endpoint += string.Format("{0}={1}", queryParam.Key, HttpUtility.HtmlEncode(queryParam.Value));

				startOfParams = false;
			}

			return endpoint;
		}

		/// <summary>
		///     Checks is a HttpResponse is valid or not.
		/// </summary>
		/// <param name="response">The HttpResponse</param>
		/// <returns>Boolean representation of the validity of the response.</returns>
		private static bool ValidateResponse(HttpResponseMessage response)
		{
			if (response == null || response.StatusCode != HttpStatusCode.OK || String.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
				return false;

			var parsedResponse = JsonConvert.DeserializeObject<Response>(response.Content.ReadAsStringAsync().Result);
			return (parsedResponse != null && (parsedResponse.Status == ResponseStatus.Okay || parsedResponse.Status == ResponseStatus.BungieOkay));
		}

		/// <summary>
		///     Checks is a HttpResponse is valid or not, and if not returns the Raw Text.
		/// </summary>
		/// <param name="response">The HttpResponse</param>
		private static string ValidateResponseAndGetRawText(HttpResponseMessage response)
		{
			return !ValidateResponse(response) ? null : response.Content.ReadAsStringAsync().Result;
		}

		/// <summary>
		///     Checks is a HttpResponse is valid or not, and parses it into a model
		/// </summary>
		/// <param name="response">The HttpResponse we are checking and parsing</param>
		/// <returns>Returns null if the response is not valid, and the parsed model if it is.</returns>
		// ReSharper disable once UnusedMember.Local
		private static TModelType ValidateAndParseResponse<TModelType>(HttpResponseMessage response)
			where TModelType : Response
		{
			if (!ValidateResponse(response))
				return null;

			try
			{
				return JsonConvert.DeserializeObject<TModelType>(response.Content.ReadAsStringAsync().Result);
			}
			catch (JsonReaderException jsonReaderException)
			{
				Debug.Write(jsonReaderException);
			}

			return null;
		}

		/// <summary>
		/// </summary>
		/// <returns></returns>
		public bool CheckApiValidity()
		{
			return true;
		}

		#endregion

		#region Enums

		public enum BlobType
		{
			Other,
			PlayerServiceRecord
		}

		#endregion

		#region General Helpers

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TBlam"></typeparam>
		/// <param name="jsonData"></param>
		/// <returns></returns>
		public TBlam ParseText<TBlam>(string jsonData)
			where TBlam : Response
		{
			if (jsonData == null) return null;

#if DEBUG
			return JsonConvert.DeserializeObject<TBlam>(jsonData);
#else
			try
			{
				return JsonConvert.DeserializeObject<TBlam>(jsonData);
			}
			catch (JsonReaderException jsonReaderException)
			{
				return null;
			}
#endif
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="blobType"></param>
		/// <param name="fileName"></param>
		/// <returns></returns>
		public string GenerateBlobContainerPath(BlobType blobType, string fileName)
		{
			string path;

			switch (blobType)
			{
				case BlobType.Other:
					path = "other";
					break;

				case BlobType.PlayerServiceRecord:
					path = "player-service-record";
					break;

				default:
					throw new ArgumentException("Invalid/Unknown Blob Type");
			}

			return string.Format("{0}/{1}.json", path, fileName);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="gamertag"></param>
		/// <returns></returns>
		public string EscapeGamertag(string gamertag)
		{
			gamertag = gamertag.ToLower();
			gamertag = gamertag.Replace(" ", "-");
			return gamertag;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TDataModel"></typeparam>
		/// <param name="blob"></param>
		/// <param name="expireLength"></param>
		/// <returns></returns>
		public Tuple<bool, TDataModel> CheckBlobValidity<TDataModel>(ICloudBlob blob, TimeSpan expireLength)
			where TDataModel : Response
		{
			if (blob == null || !blob.Exists())
				return new Tuple<bool, TDataModel>(false, null);

			var blobData = _storage.Blob.DownloadBlob<TDataModel>(blob);
			if (blobData == null) return new Tuple<bool, TDataModel>(false, null);

			if (blob.Properties.LastModified == null || DateTime.UtcNow > blob.Properties.LastModified + expireLength)
				return new Tuple<bool, TDataModel>(false, null);

			return new Tuple<bool, TDataModel>(true, blobData);
		}

		#endregion
	}
}