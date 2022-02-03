﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using GeeksCoreLibrary.Core.Cms;
using GeeksCoreLibrary.Core.DependencyInjection.Interfaces;
using GeeksCoreLibrary.Core.Extensions;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Modules.Objects.Interfaces;
using GeeksCoreLibrary.Modules.Redirect.Interfaces;
using GeeksCoreLibrary.Modules.Seo.Interfaces;
using GeeksCoreLibrary.Modules.Seo.Models;
using GeeksCoreLibrary.Modules.Templates.Enums;
using GeeksCoreLibrary.Modules.Templates.Interfaces;
using GeeksCoreLibrary.Modules.Templates.Models;
using GeeksCoreLibrary.Modules.Templates.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GeeksCoreLibrary.Modules.Templates.Services
{
    public class PagesService : IPagesService, IScopedService
    {
        private readonly ILogger<LegacyTemplatesService> logger;
        private readonly ITemplatesService templatesService;
        private readonly ISeoService seoService;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IRedirectService redirectService;
        private readonly IObjectsService objectsService;

        public PagesService(ILogger<LegacyTemplatesService> logger, IObjectsService objectsService, ITemplatesService templatesService, ISeoService seoService, IHttpContextAccessor httpContextAccessor, IRedirectService redirectService)
        {
            this.logger = logger;
            this.templatesService = templatesService;
            this.seoService = seoService;
            this.httpContextAccessor = httpContextAccessor;
            this.redirectService = redirectService;
            this.objectsService = objectsService;
        }

        /// <inheritdoc />
        public async Task<string> GetGlobalHeader(string url, List<int> javascriptTemplates, List<int> cssTemplates)
        {
            if (!Int32.TryParse(await objectsService.FindSystemObjectByDomainNameAsync("defaultheadertemplateid"), out var headerTemplateId) || headerTemplateId <= 0)
            {
                return "";
            }

            var headerRegexCheck = await objectsService.FindSystemObjectByDomainNameAsync("headerregexcheck");
            if (!String.IsNullOrWhiteSpace(url) && !String.IsNullOrWhiteSpace(headerRegexCheck) && !Regex.IsMatch(url, headerRegexCheck))
            {
                return "";
            }

            var template = await templatesService.GetTemplateAsync(headerTemplateId);
            javascriptTemplates.AddRange(template.JavascriptTemplates);
            cssTemplates.AddRange(template.CssTemplates);
            logger.LogDebug($"Default header template loaded: '{headerTemplateId}'");
            return template.Content;
        }

        /// <inheritdoc />
        public async Task<string> GetGlobalFooter(string url, List<int> javascriptTemplates, List<int> cssTemplates)
        {
            if (!Int32.TryParse(await objectsService.FindSystemObjectByDomainNameAsync("defaultfootertemplateid"), out var footerTemplateId) || footerTemplateId <= 0)
            {
                return "";
            }

            var headerRegexCheck = await objectsService.FindSystemObjectByDomainNameAsync("footerregexcheck");
            if (!String.IsNullOrWhiteSpace(url) && !String.IsNullOrWhiteSpace(headerRegexCheck) && !Regex.IsMatch(url, headerRegexCheck))
            {
                return "";
            }

            var template = await templatesService.GetTemplateAsync(footerTemplateId);
            javascriptTemplates.AddRange(template.JavascriptTemplates);
            cssTemplates.AddRange(template.CssTemplates);
            logger.LogDebug($"Default footer template loaded: '{footerTemplateId}'");
            return template.Content;
        }

        /// <inheritdoc />
        public async Task<PageViewModel> CreatePageViewModelAsync(List<string> externalCss, List<int> cssTemplates, List<string> externalJavascript, List<int> javascriptTemplates, string newBodyHtml)
        {
            var viewModel = new PageViewModel();

            // Add CSS for all pages.
            var generalCss = await templatesService.GetGeneralTemplateValueAsync(TemplateTypes.Css);
            externalCss.AddRange(generalCss.ExternalFiles);
            if (!String.IsNullOrWhiteSpace(generalCss.Content))
            {
                viewModel.Css.GeneralCssFileName = $"/css/gcl_general.css?c={generalCss.LastChangeDate:yyyyMMddHHmmss}";
            }

            // Add css for this specific page.
            if (cssTemplates.Count > 0)
            {
                var standardCssTemplates = new List<int>();
                var inlineHeadCssTemplates = new List<string>();
                var syncFooterCssTemplates = new List<int>();
                var asyncFooterCssTemplates = new List<int>();

                var templates = (await templatesService.GetTemplatesAsync(cssTemplates, true)).Where(t => t.Type == TemplateTypes.Css).ToList();
                foreach (var template in templates)
                {
                    externalCss.AddRange(template.ExternalFiles);
                    switch (template.InsertMode)
                    {
                        case ResourceInsertModes.Standard:
                            standardCssTemplates.Add(template.Id);
                            break;
                        case ResourceInsertModes.InlineHead:
                            inlineHeadCssTemplates.Add(template.Content);
                            break;
                        case ResourceInsertModes.AsyncFooter:
                            asyncFooterCssTemplates.Add(template.Id);
                            break;
                        case ResourceInsertModes.SyncFooter:
                            syncFooterCssTemplates.Add(template.Id);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                var lastChanged = !templates.Any() ? DateTime.Now : templates.Max(t => t.LastChanged);
                var standardSuffix = $"c={lastChanged:yyyyMMddHHmmss}";

                if (standardCssTemplates.Any())
                {
                    viewModel.Css.PageStandardCssFileName = $"/css/gclcss_{String.Join("_", standardCssTemplates)}.css?mode=Standard&{standardSuffix}";
                }

                if (inlineHeadCssTemplates.Any())
                {
                    viewModel.Css.PageInlineHeadCss = String.Join(Environment.NewLine, inlineHeadCssTemplates);
                }

                if (asyncFooterCssTemplates.Any())
                {
                    viewModel.Css.PageAsyncFooterCssFileName = $"/css/gclcss_{String.Join("_", asyncFooterCssTemplates)}.css?mode=AsyncFooter&{standardSuffix}";
                }

                if (syncFooterCssTemplates.Any())
                {
                    viewModel.Css.PageSyncFooterCssFileName = $"/css/gclcss_{String.Join("_", asyncFooterCssTemplates)}.css?mode=SyncFooter&{standardSuffix}";
                }
            }

            // Add Javascript for all pages.
            var moveAllJavascriptToBottom = (await objectsService.FindSystemObjectByDomainNameAsync("javascriptmovetobottom", "false")).Equals("true", StringComparison.OrdinalIgnoreCase);
            var generalJavascript = await templatesService.GetGeneralTemplateValueAsync(TemplateTypes.Js);
            externalJavascript.AddRange(generalJavascript.ExternalFiles);
            if (!String.IsNullOrWhiteSpace(generalJavascript.Content))
            {
                if (moveAllJavascriptToBottom)
                {
                    viewModel.Javascript.GeneralFooterJavascriptFileName = $"/scripts/gcl_general.js?c={generalJavascript.LastChangeDate:yyyyMMddHHmmss}";
                }
                else
                {
                    viewModel.Javascript.GeneralJavascriptFileName = $"/scripts/gcl_general.js?c={generalJavascript.LastChangeDate:yyyyMMddHHmmss}";
                }
            }

            // Add Javascript for this specific page.
            if (javascriptTemplates.Count > 0)
            {
                var standardJavascriptTemplates = new List<int>();
                var inlineHeadJavascriptTemplates = new List<string>();
                var syncFooterJavascriptTemplates = new List<int>();
                var asyncFooterJavascriptTemplates = new List<int>();

                var templates = (await templatesService.GetTemplatesAsync(javascriptTemplates, true)).Where(t => t.Type == TemplateTypes.Js).ToList();
                foreach (var template in templates)
                {
                    externalJavascript.AddRange(template.ExternalFiles);
                    switch (template.InsertMode)
                    {
                        case ResourceInsertModes.Standard:
                            if (moveAllJavascriptToBottom)
                            {
                                syncFooterJavascriptTemplates.Add(template.Id);
                            }
                            else
                            {
                                standardJavascriptTemplates.Add(template.Id);
                            }

                            break;
                        case ResourceInsertModes.InlineHead:
                            inlineHeadJavascriptTemplates.Add(template.Content);
                            break;
                        case ResourceInsertModes.AsyncFooter:
                            asyncFooterJavascriptTemplates.Add(template.Id);
                            break;
                        case ResourceInsertModes.SyncFooter:
                            syncFooterJavascriptTemplates.Add(template.Id);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                var lastChanged = !templates.Any() ? DateTime.Now : templates.Max(t => t.LastChanged);
                var standardSuffix = $"c={lastChanged:yyyyMMddHHmmss}";

                if (standardJavascriptTemplates.Any())
                {
                    viewModel.Javascript.PageStandardJavascriptFileName = $"/scripts/gcljs_{String.Join("_", standardJavascriptTemplates)}.js?mode=Standard&{standardSuffix}";
                }

                if (inlineHeadJavascriptTemplates.Any())
                {
                    viewModel.Javascript.PageInlineHeadJavascript = String.Join(Environment.NewLine, inlineHeadJavascriptTemplates);
                }

                if (asyncFooterJavascriptTemplates.Any())
                {
                    viewModel.Javascript.PageAsyncFooterJavascriptFileName = $"/scripts/gcljs_{String.Join("_", asyncFooterJavascriptTemplates)}.js?mode=AsyncFooter&{standardSuffix}";
                }

                if (syncFooterJavascriptTemplates.Any())
                {
                    viewModel.Javascript.PageSyncFooterJavascriptFileName = $"/scripts/gcljs_{String.Join("_", syncFooterJavascriptTemplates)}.js?mode=SyncFooter&{standardSuffix}";
                }
            }

            // Get SEO data and replace the body with data from seo module if applicable.
            if (await seoService.SeoModuleIsEnabledAsync())
            {
                viewModel.MetaData = await seoService.GetSeoDataForPageAsync(HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext));

                if (newBodyHtml.Contains("[{seomodule_", StringComparison.OrdinalIgnoreCase))
                {
                    if (String.IsNullOrWhiteSpace(viewModel.MetaData?.SeoText))
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_content}\|(.*?)\]", "$1");
                    }
                    else
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_content}\|(.*?)\]", viewModel.MetaData.SeoText);
                        newBodyHtml = newBodyHtml.ReplaceCaseInsensitive("[{seomodule_content}]", viewModel.MetaData.SeoText);
                    }

                    if (String.IsNullOrWhiteSpace(viewModel.MetaData?.H1Text))
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h1header}\|(.*?)\]", "$1");
                    }
                    else
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h1header}\|(.*?)\]", viewModel.MetaData.H1Text);
                        newBodyHtml = newBodyHtml.ReplaceCaseInsensitive("[{seomodule_h1header}]", viewModel.MetaData.H1Text);
                    }

                    if (String.IsNullOrWhiteSpace(viewModel.MetaData?.H2Text))
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h2header}\|(.*?)\]", "$1");
                    }
                    else
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h2header}\|(.*?)\]", viewModel.MetaData.H2Text);
                        newBodyHtml = newBodyHtml.ReplaceCaseInsensitive("[{seomodule_h2header}]", viewModel.MetaData.H2Text);
                    }

                    if (String.IsNullOrWhiteSpace(viewModel.MetaData?.H3Text))
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h3header}\|(.*?)\]", "$1");
                    }
                    else
                    {
                        newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_h3header}\|(.*?)\]", viewModel.MetaData.H3Text);
                        newBodyHtml = newBodyHtml.ReplaceCaseInsensitive("[{seomodule_h3header}]", viewModel.MetaData.H3Text);
                    }
                }
            }

            // Handle any left over seo module things.
            if (newBodyHtml.Contains("[{seomodule_"))
            {
                newBodyHtml = newBodyHtml.ReplaceCaseInsensitive("[{seomodule_content}]", "");
                newBodyHtml = Regex.Replace(newBodyHtml, @"\[{seomodule_.*?}\|(.*?)\]", "$1");
            }

            // Check if some component is adding external JavaScript libraries to the page.
            var externalScripts = externalJavascript.Select(ej => new JavaScriptResource { Uri = new Uri(ej) }).ToList();
            if (httpContextAccessor.HttpContext?.Items[CmsSettings.ExternalJavaScriptLibrariesFromComponentKey] is List<JavaScriptResource> componentExternalJavaScriptLibraries)
            {
                foreach (var externalLibrary in componentExternalJavaScriptLibraries.Where(externalLibrary => !externalScripts.Any(l => l.Uri.AbsoluteUri.Equals(externalLibrary.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))))
                {
                    externalScripts.Add(externalLibrary);
                }
            }

            viewModel.Css.ExternalCss = externalCss;
            viewModel.Javascript.ExternalJavascript = externalScripts;
            viewModel.Body = newBodyHtml;

            // Add viewport.
            var viewportSystemObjectValue = await objectsService.FindSystemObjectByDomainNameAsync("metatag_viewport", "false");
            var viewportValueIsBoolean = Boolean.TryParse(viewportSystemObjectValue, out var viewportBooleanValue);
            var viewportValueIsInt = Int32.TryParse(viewportSystemObjectValue, out var viewportIntValue);

            if (viewportValueIsBoolean || viewportValueIsInt)
            {
                // Value is either a boolean value or integer value.
                if (viewportBooleanValue || viewportIntValue > 0)
                {
                    viewModel.MetaData.MetaTags["viewport"] = "height=device-height,width=device-width,initial-scale=1.0,maximum-scale=5.0";
                }
            }
            else if (!String.IsNullOrWhiteSpace(viewportSystemObjectValue))
            {
                // Viewport is a custom string; use the value of the system object.
                viewModel.MetaData.MetaTags["viewport"] = viewportSystemObjectValue;
            }

            // Check if some component is adding SEO data to the page.
            if (httpContextAccessor.HttpContext.Items[CmsSettings.PageMetaDataFromComponentKey] is PageMetaDataModel componentSeoData)
            {
                if (componentSeoData.MetaTags != null && componentSeoData.MetaTags.Any())
                {
                    foreach (var (key, value) in componentSeoData.MetaTags.Where(metaTag => !viewModel.MetaData.MetaTags.ContainsKey(metaTag.Key) || String.IsNullOrWhiteSpace(viewModel.MetaData.MetaTags[metaTag.Key])))
                    {
                        viewModel.MetaData.MetaTags[key] = value;
                    }
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.PageTitle) && String.IsNullOrWhiteSpace(viewModel.MetaData.PageTitle))
                {
                    viewModel.MetaData.PageTitle = componentSeoData.PageTitle;
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.Canonical) && String.IsNullOrWhiteSpace(viewModel.MetaData.Canonical))
                {
                    viewModel.MetaData.Canonical = componentSeoData.Canonical;
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.H1Text) && String.IsNullOrWhiteSpace(viewModel.MetaData.H1Text))
                {
                    viewModel.MetaData.H1Text = componentSeoData.H1Text;
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.H2Text) && String.IsNullOrWhiteSpace(viewModel.MetaData.H2Text))
                {
                    viewModel.MetaData.H2Text = componentSeoData.H2Text;
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.H3Text) && String.IsNullOrWhiteSpace(viewModel.MetaData.H3Text))
                {
                    viewModel.MetaData.H3Text = componentSeoData.H3Text;
                }

                if (!String.IsNullOrWhiteSpace(componentSeoData.SeoText) && String.IsNullOrWhiteSpace(viewModel.MetaData.SeoText))
                {
                    viewModel.MetaData.SeoText = componentSeoData.SeoText;
                }
            }

            // See if we need to add canonical to self, but only if no other canonical has been added yet.
            if (String.IsNullOrWhiteSpace(viewModel.MetaData.Canonical))
            {
                var canonicalSetting = await objectsService.FindSystemObjectByDomainNameAsync("always_add_canonical_to_self");
                if (canonicalSetting.Equals("true", StringComparison.OrdinalIgnoreCase) || canonicalSetting.Equals("1", StringComparison.Ordinal))
                {
                    var canonicalUrl = HttpContextHelpers.GetOriginalRequestUriBuilder(httpContextAccessor.HttpContext);
                    var parametersToIncludeForCanonical = (await objectsService.FindSystemObjectByDomainNameAsync("include_parameters_canonical")).Split(",", StringSplitOptions.RemoveEmptyEntries);

                    if (!parametersToIncludeForCanonical.Any())
                    {
                        canonicalUrl.Query = "";
                    }
                    else
                    {
                        // Remove the query string from the canonical, except for keys that have been set in the settings.
                        var queryString = HttpUtility.ParseQueryString(canonicalUrl.Query);
                        var queryStringsToRemove = queryString.AllKeys.Where(k => !parametersToIncludeForCanonical.Any(p => p.Equals(k)));
                        foreach (var key in queryStringsToRemove)
                        {
                            queryString.Remove(key);
                        }

                        canonicalUrl.Query = queryString.ToString();

                        // If the current URL does not exist in the SEO module, check if we need to strip a part of it before adding it as a canonical.
                        var canonicalPathEnd = (await objectsService.FindSystemObjectByDomainNameAsync("canonical_path_end")).Split(",", StringSplitOptions.RemoveEmptyEntries);
                        foreach (var urlValue in canonicalPathEnd)
                        {
                            var index = canonicalUrl.Path.IndexOf(urlValue, StringComparison.OrdinalIgnoreCase);
                            if (index == -1)
                            {
                                continue;
                            }

                            // Strip the value of canonicalPathEnd and everything after that from the current URL, that will be the new canonical URL.
                            canonicalUrl.Path = canonicalUrl.Path.Substring(0, index);

                            if (!canonicalUrl.Path.EndsWith("/") && await redirectService.ShouldRedirectToUrlWithTrailingSlashAsync())
                            {
                                canonicalUrl.Path += "/";
                            }

                            // Only do this for the first occurrence.
                            break;
                        }
                    }

                    viewModel.MetaData.Canonical = canonicalUrl.ToString();
                }
            }

            // Check if there is a global meta title suffix set and add it to the final page title.
            var globalPageTitleSuffix = await objectsService.FindSystemObjectByDomainNameAsync("global_meta_title_suffix");
            if (String.IsNullOrWhiteSpace(viewModel.MetaData.PageTitle) || (!String.IsNullOrWhiteSpace(globalPageTitleSuffix) && !viewModel.MetaData.PageTitle.EndsWith(globalPageTitleSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                viewModel.MetaData.GlobalPageTitleSuffix = globalPageTitleSuffix;
            }

            // Load all Google Analytics related stuff.
            AddGoogleAnalyticsToViewModel(viewModel);

            // Check for additional plugins to load (like Wiser Search, Zopim, etc.).
            AddPluginScripts(viewModel);

            return viewModel;
        }

        /// <summary>
        /// Sets various Google Analytics tracking scripts based on the customer's settings.
        /// </summary>
        /// <param name="viewModel">The <see cref="PageViewModel"/> that will be updated.</param>
        private async void AddGoogleAnalyticsToViewModel(PageViewModel viewModel)
        {
            var inlineHeadJavaScript = new StringBuilder();
            var inlineBodyNoScript = new StringBuilder();

            // Universal Analytics (Google Analytics 3).
            var universalAnalyticsCode = await objectsService.FindSystemObjectByDomainNameAsync("GoAnCode");
            var universalAnalyticsEnabled = !String.IsNullOrWhiteSpace(universalAnalyticsCode);
            if (universalAnalyticsEnabled)
            {
                inlineHeadJavaScript.AppendLine("(function(i,s,o,g,r,a,m){i['GoogleAnalyticsObject']=r;i[r]=i[r]||function(){");
                inlineHeadJavaScript.AppendLine("(i[r].q=i[r].q||[]).push(arguments)},i[r].l=1*new Date();a=s.createElement(o),");
                inlineHeadJavaScript.AppendLine("m=s.getElementsByTagName(o)[0];a.async=1;a.src=g;m.parentNode.insertBefore(a,m)");
                inlineHeadJavaScript.AppendLine("})(window,document,'script','https://www.google-analytics.com/analytics.js','ga');");
                inlineHeadJavaScript.AppendLine();
                inlineHeadJavaScript.AppendLine($"ga('create', '{universalAnalyticsCode}', 'auto');");
            }

            // Google Analytics 4.
            var googleAnalytics4Code = await objectsService.FindSystemObjectByDomainNameAsync("GoAn4Code");
            var googleAnalytics4Enabled = !String.IsNullOrWhiteSpace(googleAnalytics4Code);
            if (googleAnalytics4Enabled)
            {
                viewModel.GoogleAnalytics.HeadJavaScriptResources.Add(new JavaScriptResource
                {
                    Uri = new Uri($"https://www.googletagmanager.com/gtag/js?id={googleAnalytics4Code}"),
                    Async = true
                });

                inlineHeadJavaScript.AppendLine("window.dataLayer = window.dataLayer || [];");
                inlineHeadJavaScript.AppendLine("function gtag(){dataLayer.push(arguments);}");
                inlineHeadJavaScript.AppendLine("gtag('js', new Date());");
                inlineHeadJavaScript.AppendLine();
                inlineHeadJavaScript.AppendLine($"gtag('config', '{googleAnalytics4Code}');");
            }

            // Google Tag Manager.
            var googleTagManagerCode = await objectsService.FindSystemObjectByDomainNameAsync("GoAnTagManagerId");
            var googleTagManagerEnabled = !String.IsNullOrWhiteSpace(googleTagManagerCode);
            if (googleTagManagerEnabled)
            {
                inlineHeadJavaScript.AppendLine("(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':");
                inlineHeadJavaScript.AppendLine("new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],");
                inlineHeadJavaScript.AppendLine("j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src=");
                inlineHeadJavaScript.AppendLine("'https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);");
                inlineHeadJavaScript.AppendLine($"}})(window,document,'script','dataLayer','{googleTagManagerCode}');");

                inlineBodyNoScript.AppendLine($"<iframe src=\"https://www.googletagmanager.com/ns.html?id={googleTagManagerCode}\" height=\"0\" width=\"0\" style=\"display:none;visibility:hidden\"></iframe>");
            }

            // Additional settings and plugins for Universal Analytics and send page view.
            if (universalAnalyticsEnabled)
            {
                var useDisplayFeatures = (await objectsService.FindSystemObjectByDomainNameAsync("GoAnDisplayFeatures")).InList(StringComparer.OrdinalIgnoreCase, "true", "1");
                if (useDisplayFeatures)
                {
                    inlineHeadJavaScript.AppendLine("ga('require', 'displayfeatures');");
                }

                var useEnhancedLinkAttribution = (await objectsService.FindSystemObjectByDomainNameAsync("GoAnUseLinkID")).InList(StringComparer.OrdinalIgnoreCase, "true", "1");
                if (useEnhancedLinkAttribution)
                {
                    inlineHeadJavaScript.AppendLine("ga('require', 'linkid');");
                }

                var anonymizeIp = (await objectsService.FindSystemObjectByDomainNameAsync("GoAnAnonymizeIp")).InList(StringComparer.OrdinalIgnoreCase, "true", "1");
                if (anonymizeIp)
                {
                    inlineHeadJavaScript.AppendLine("ga('set', 'anonymizeIp', true);");
                }

                // Page view is sent last because the plugins and settings can influence how the page view works.
                inlineHeadJavaScript.AppendLine("ga('send', 'pageview');");
            }

            // Set the scripts to their respective properties.
            if (inlineHeadJavaScript.Length > 0)
            {
                viewModel.GoogleAnalytics.InlineHeadJavaScript = inlineHeadJavaScript.ToString();
            }
            if (inlineBodyNoScript.Length > 0)
            {
                viewModel.GoogleAnalytics.InlineBodyNoScript = inlineBodyNoScript.ToString();
            }
        }

        /// <summary>
        /// Adds plugin scripts to the view model's Javascript settings.
        /// </summary>
        /// <param name="viewModel">The <see cref="PageViewModel"/> that will be updated.</param>
        private async void AddPluginScripts(PageViewModel viewModel)
        {
            var wiserSearchScript = await objectsService.FindSystemObjectByDomainNameAsync("WiserSearchScript");

            if (!String.IsNullOrWhiteSpace(wiserSearchScript))
            {
                if (wiserSearchScript.StartsWith("<script", StringComparison.OrdinalIgnoreCase))
                {
                    wiserSearchScript = Regex.Replace(wiserSearchScript, "^<script.*?>(?<script>.*?)</script>", "${script}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                }

                viewModel.Javascript.PagePluginInlineJavascriptSnippets.Add(wiserSearchScript);
            }
        }
    }
}
