﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using TokenManager.Collections;
using TokenManager.Data.Interfaces;
using TokenManager.Data.Tokens;
using TokenManager.Handlers.ContentTree;
using TokenManager.Handlers.TokenOperations;
using TokenManager.Management;

namespace TokenManager.Handlers
{
	/// <summary>
	/// base handler for http requests to the TokenManager 
	/// </summary>
	public class TokenManagerHandler : BaseHttpHandler
	{
		private readonly string _prefix;

		public TokenManagerHandler() : this(null) { }

		/// <summary>
		/// keeps track of the user's currently selected token using the session ID
		/// </summary>
		private static readonly Dictionary<string, string> _userCurrentToken = new Dictionary<string, string>();

		public TokenManagerHandler(string prefix)
		{
			_prefix = prefix;
		}

		/// <summary>
		/// gets post data from post request
		/// </summary>
		/// <param name="context"></param>
		/// <returns>dynamic object containing post javascript object</returns>
		public static dynamic GetPostData(HttpContextBase context)
		{
			using (StreamReader sr = new StreamReader(context.Request.InputStream))
			{
				dynamic ret = JsonNetWrapper.DeserializeObject<ExpandoObject>(sr.ReadToEnd());
				try
				{
					HttpContext.Current.Items["datasource"] = ret.sc_itemid;
				}
				catch (RuntimeBinderException)
				{
				}
				return ret;
			}
		}

		public static Item GetDatasourceItem()
		{
			string id = HttpContext.Current.Items["datasource"]?.ToString();
			if (!string.IsNullOrWhiteSpace(id))
			{
				return TokenKeeper.CurrentKeeper.GetDatabase().GetItem(id);
			}
			return null;
		}

		public static IToken GetSelectedToken(HttpContextBase context)
		{
			return TokenKeeper.CurrentKeeper.ParseITokenFromText(_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value]);
		}

		/// <summary>
		/// base HTTP request
		/// </summary>
		/// <param name="context"></param>
		public override void ProcessRequest(HttpContextBase context)
		{
			try
			{
				var path = context.Request.AppRelativeCurrentExecutionFilePath;
				var fileName = Path.GetFileName(path);
				if (fileName == null)
				{
					NotFound(context);
					return;
				}

				var file = fileName.ToLowerInvariant();
				if (_prefix.Equals(file, StringComparison.CurrentCultureIgnoreCase))
				{
					file = "";
				}
				if (context.Request.Cookies["ASP.NET_SessionId"] == null)
				{
					context.Response.Redirect("/sitecore/login");
					return;
				}
				if (string.IsNullOrWhiteSpace(file))
				{
					string html = GetResource("index.html").Replace("<datasource></datasource>", $"<script>var tmDatasource='{context.Request.QueryString["sc_itemid"]}';</script>");
					if (_userCurrentToken.ContainsKey(context.Request.Cookies["ASP.NET_SessionId"].Value) && !string.IsNullOrWhiteSpace(_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value]))
						html = html.Replace("<preset></preset>", "<script>var tmPreset=true;</script>");
					ReturnResponse(context, html, "text/html");
				}
				else if (file.EndsWith(".js"))
					ReturnResource(context, file, "application/javascript");
				else if (file.EndsWith(".html"))
					ReturnResource(context, file, "text/html");
				else if (file == "contenteditor.css")
					ReturnResponse(context, $".token-manager-token{{{TokenKeeper.CurrentKeeper.TokenCss}}}",
						"text/css");
				else if (file.EndsWith(".css"))
					ReturnResource(context, file, "text/css");
				else if (file.EndsWith(".gif"))
					ReturnImage(context, file, ImageFormat.Gif, "image/gif");
				else if (file.EndsWith(".png"))
					ReturnImage(context, file, ImageFormat.Png, "image/png");
				else if (file.EndsWith(".jpg"))
					ReturnImage(context, file, ImageFormat.Jpeg, "image/jpg");
				else if (file == "categories.json")
					ReturnJson(context, GetTokenCategories());
				else if (file == "tokens.json")
					ReturnJson(context, GetTokens(context));
				else if (file == "tokenidentifier.json")
					ReturnJson(context, GetTokenIdentifier(context));
				else if (file == "tokenincorporator.json")
					ReturnJson(context, IncorporateToken(context));
				else if (file == "databases.json")
					ReturnJson(context, GetDatabases());
				else if (file == "sitecoretokencollections.json")
					ReturnJson(context, GetSitecoreTokenCollectionNames(context));
				else if (file == "incorporatetokens.json")
					ReturnJson(context, IncorporateToken(context));
				else if (file == "issitecorecollection.json")
					ReturnJson(context, IsSitecoreCollection(context));
				else if (file == "unziptoken.json")
					ReturnJson(context, UnzipToken(context));
				else if (file == "tokenstats.json")
					ReturnJson(context, GetTokenStats(context));
				else if (file == "contenttree.json")
					ReturnJson(context, GetContentTree(context));
				else if (file == "contenttreeselectedrelated.json")
					ReturnJson(context, GetContentSelectedRelated(context));
				else if (file == "tokenselected.json")
					ReturnJson(context, GetRichTextSelectedToken(context));
				else if (file == "anytokensvalid.json")
					ReturnJson(context, TokenKeeper.CurrentKeeper.GetTokenCollections().Any(x => x.IsCurrentContextValid(TokenKeeper.CurrentKeeper.GetDatabase().GetItem(context.Request.QueryString["sc_itemid"]))));
				else if (file == "tokenvalid.json")
					ReturnJson(context, IsTokenValid(context));
				else if (file == "tokensetup.json")
					ReturnJson(context, SetupTokenTracking(context));
				else
					NotFound(context);
			}
			catch (Exception e)
			{
				Log.Error("TokenManager failed to return the proper resource", e, this);
				Error(context, e);
			}
		}

		private object SetupTokenTracking(HttpContextBase context)
		{
			var data = GetPostData(context);
			data.token = data.token.Replace("lttt", "<").Replace("gttt", ">").Replace("amppp", "&");
			data.preset = data.preset.Replace("lttt", "<").Replace("gttt", ">").Replace("amppp", "&");
			_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value] = data.token;
			if (!string.IsNullOrWhiteSpace(data.preset))
			{
				var selectedToken =
					TokenKeeper.CurrentKeeper.TokenProperties(_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value]);
				var presetToken =
					TokenKeeper.CurrentKeeper.TokenProperties(data.preset);
				if (selectedToken["Category"] != presetToken["Category"] || selectedToken["Token"] != presetToken["Token"])
					_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value] = data.preset;
			}
			return true;
		}

		/// <summary>
		/// returns true if the requested token is valid in this instance
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private object IsTokenValid(HttpContextBase context)
		{
			var tokenData = HttpUtility.ParseQueryString(context.Request.Form["tokenString"]);
			HttpContext.Current.Items["datasource"] = context.Request.Form["datasource"];
			bool? catValid = TokenKeeper.CurrentKeeper.GetTokenCollections().FirstOrDefault(x => x.GetCollectionLabel() == tokenData["category"])?
				.IsCurrentContextValid(GetDatasourceItem());
			if (catValid == null || !catValid.Value)
				return false;
			var token = TokenKeeper.CurrentKeeper.GetToken(tokenData["category"], tokenData["token"]) as AutoToken;
			if (token == null)
				return true;
			return token.IsCurrentContextValid(GetDatasourceItem());
		}

		/// <summary>
		/// returns true if the current item id is a descendant of the current id
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static bool GetContentSelectedRelated(HttpContextBase context)
		{
			var data = GetPostData(context);
			var db = TokenKeeper.CurrentKeeper.GetDatabase();
			Item current = db.GetItem(new ID(data.currentId));
			Item selected = db.GetItem(new ID(data.selectedId));
			return selected?.Paths.FullPath.StartsWith(current?.Paths.FullPath ?? "") ?? false;
		}

		/// <summary>
		/// Get the token that was selected in the rich text editor
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private dynamic GetRichTextSelectedToken(HttpContextBase context)
		{
			dynamic ret = new ExpandoObject();
			var props = TokenKeeper.CurrentKeeper.TokenProperties(_userCurrentToken[context.Request.Cookies["ASP.NET_SessionId"].Value]);
			if (props["Category"] == null || props["Token"] == null)
				return ret;
			ret.Category = props["Category"];
			ret.Token = props["Token"];
			props.Remove("Category");
			props.Remove("Token");
			ret.Fields = GetTokenExtraData(ret.Category, ret.Token);
			ret.FieldValues = props.Cast<string>().ToDictionary(p => p, p => GetExtraDataConcrete(ret.Fields, p, props[p]));
			return ret;
			;
		}

		/// <summary>
		/// Converts all the string values stored in the token's extra data to their concrete objects
		/// </summary>
		/// <param name="data"></param>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		private static object GetExtraDataConcrete(IEnumerable<ITokenData> data, string name, string value)
		{

			var currentData = data.FirstOrDefault(d => d.Name == name);
			if (currentData != null)
			{
				return currentData.GetValue(value);
			}
			return value;
		}

		/// <summary>
		/// Gathers the specifications for the extra data from the token implementation
		/// </summary>
		/// <param name="category"></param>
		/// <param name="tokenName"></param>
		/// <returns></returns>
		private static object GetTokenExtraData(string category, string tokenName)
		{
			IToken token = TokenKeeper.CurrentKeeper.GetToken(category, tokenName);
			return token?.ExtraData();
		}

		/// <summary>
		/// returns the contetn tree level for the given id
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object GetContentTree(HttpContextBase context)
		{
			var data = GetPostData(context);
			return new ContentTreeNode(Factory.GetDatabase(data.database).GetItem(new ID(data.id)), true);
		}

		/// <summary>
		/// Token Stats Request
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object GetTokenStats(HttpContextBase context)
		{
			var data = GetPostData(context);
			var tokenStats = new TokenStats(data.category, data.token);
			return tokenStats.GetStats();
		}

		/// <summary>
		/// unzip request
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object UnzipToken(HttpContextBase context)
		{
			var data = GetPostData(context);
			var unzipper = new TokenUnzipper(data.root, data.category, data.token, data.replaceWithValue);
			return unzipper.Unzip(data.preview);
		}

		/// <summary>
		/// is the collection a sitecore collection
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object IsSitecoreCollection(HttpContextBase context)
		{
			var data = GetPostData(context);
			return
				TokenKeeper.CurrentKeeper.GetTokenCollection<IToken>(data.category) is SimpleSitecoreTokenCollection;
		}

		/// <summary>
		/// get all sitecore managed collectiongs
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object GetSitecoreTokenCollectionNames(HttpContextBase context)
		{
			var data = GetPostData(context);
			string database = data.database;
			return TokenKeeper.CurrentKeeper.GetTokenCollections().Where(c => c is SitecoreTokenCollection<IToken> && ((SitecoreTokenCollection<IToken>)c).IsAvailableOnDatabase(database)).Select(c => c.GetCollectionLabel());
		}

		/// <summary>
		/// get all database names
		/// </summary>
		/// <returns></returns>
		private static object GetDatabases()
		{
			return Factory.GetDatabases().Select(d => d.Name).ToArray();
		}

		/// <summary>
		/// token incorporator request
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object IncorporateToken(HttpContextBase context)
		{
			var data = GetPostData(context);
			var incorporator = new TokenIncorporator(data.root, data.category, data.tokenName, data.tokenValue);
			return incorporator.Incorporate(data.preview, data.type);
		}

		/// <summary>
		/// token idenfier request
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object GetTokenIdentifier(HttpContextBase context)
		{
			var data = GetPostData(context);
			dynamic ret = new ExpandoObject();
			if (((IDictionary<string, object>)data).ContainsKey("data"))
			{
				ret.TokenIdentifier = TokenKeeper.CurrentKeeper.GetTokenIdentifier(data.category, data.token, data.data);
			}
			else
			{
				ret.TokenIdentifier = TokenKeeper.CurrentKeeper.GetTokenIdentifier(data.category, data.token, null);
				ret.Fields = GetTokenExtraData(data.category, data.token);
			}
			return ret;
		}

		/// <summary>
		/// token list request for given category
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		private static object GetTokens(HttpContextBase context)
		{
			var data = GetPostData(context);
			return ((IEnumerable<IToken>)TokenKeeper.CurrentKeeper.GetTokens(data.category)).Where(x => !(x is AutoToken) || ((AutoToken)x).IsCurrentContextValid(GetDatasourceItem())).Select(ResolveToken);
		}

		private static dynamic ResolveToken(IToken token)
		{
			dynamic ret = new ExpandoObject();
			ret.Label = token.Token;
			ret.Icon = "";
			ID tokenBackingItemId = token.GetBackingItemId();
			if (tokenBackingItemId != (ID)null)
			{
				var tokenItem = TokenKeeper.CurrentKeeper.GetDatabase().GetItem(token.GetBackingItemId());
				ret.Icon = "";
				if (tokenItem != null)
				{
					ret.Icon = GetIcon(tokenItem[FieldIDs.Icon]);
					if (string.IsNullOrWhiteSpace(ret.Icon))
						ret.Icon = GetIcon(tokenItem.Template.InnerItem[FieldIDs.Icon]);
				}
			}
			AutoToken autoToken = token as AutoToken;
			if (autoToken != null)
				ret.Icon = GetIcon(autoToken.TokenIcon);
			if (string.IsNullOrWhiteSpace(ret.Icon))
				ret.Icon = TokenKeeper.IsSc8 ? GetIcon("Office/32x32/package.png") : GetIcon("People/32x32/cube_green.png");
			return ret;

		}

		/// <summary>
		/// token category name list request
		/// </summary>
		/// <returns></returns>
		private static object GetTokenCategories()
		{
			//var db = TokenKeeper.CurrentKeeper.GetDatabase();
			//var item = GetDatasourceItem();
			//if (item == null)
			//	item = Context.Item;
			//if (item == null)
			//{
			//	var itemId = HttpContext.Current.Request.QueryString.Get("sc_itemid");
			//	if (string.IsNullOrWhiteSpace(itemId))
			//		return null;
			//	item = db.GetItem(itemId);
			//	if (item != null)
			//	{
			//		Context.Item = item;
			//		if (item.IsTokenManagerItem())
			//			return null;
			//	}
			//}
			return TokenKeeper.CurrentKeeper.GetTokenCollections().Select(GetTokenLabelAndIcon);
		}

		/// <summary>
		/// returns the label and icon of the token collection
		/// </summary>
		/// <param name="arg"></param>
		/// <returns></returns>
		private static dynamic GetTokenLabelAndIcon(ITokenCollection<IToken> arg)
		{
			dynamic ret = new ExpandoObject();
			ret.Label = arg.GetCollectionLabel();
			ret.Icon =arg.SitecoreIcon;
			if (string.IsNullOrWhiteSpace(ret.Icon))
				ret.Icon = TokenKeeper.IsSc8 ? GetIcon("Office/32x32/package.png") : GetIcon("People/32x32/cube_green.png");
			else
				ret.Icon = GetIcon(ret.Icon);
			return ret;
		}
		private static string GetIcon(string icon)
		{
			return GetSrc(ThemeManager.GetImage(icon, 32, 32));
		}
		private static string GetSrc(string imgTag)
		{
			int i1 = imgTag.IndexOf("src=\"", StringComparison.Ordinal) + 5;
			if (i1 == 4)
				return "";
			int i2 = imgTag.IndexOf("\"", i1, StringComparison.Ordinal);
			if (i2 <= i1 || i2 == -1)
				return "";
			return imgTag.Substring(i1, i2 - i1);
		}

	}
}
