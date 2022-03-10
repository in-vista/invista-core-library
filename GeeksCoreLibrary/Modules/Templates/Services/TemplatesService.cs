﻿using GeeksCoreLibrary.Core.Enums;
using GeeksCoreLibrary.Core.Extensions;
using GeeksCoreLibrary.Core.Models;
using GeeksCoreLibrary.Modules.GclReplacements.Interfaces;
using GeeksCoreLibrary.Modules.Templates.Enums;
using GeeksCoreLibrary.Modules.Templates.Extensions;
using GeeksCoreLibrary.Modules.Templates.Interfaces;
using GeeksCoreLibrary.Modules.Templates.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeeksCoreLibrary.Components.Filter.Interfaces;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Modules.Databases.Interfaces;
using GeeksCoreLibrary.Modules.Languages.Interfaces;
using GeeksCoreLibrary.Modules.Objects.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Newtonsoft.Json.Linq;
using Template = GeeksCoreLibrary.Modules.Templates.Models.Template;

namespace GeeksCoreLibrary.Modules.Templates.Services
{
    /// <summary>
    /// This class provides template caching, template replacements and rendering
    /// for all types of templates, like CSS, JS, Query's and HTML templates.
    /// </summary>
    public class TemplatesService : ITemplatesService
    {
        private readonly GclSettings gclSettings;
        private readonly ILogger<LegacyTemplatesService> logger;
        private readonly IDatabaseConnection databaseConnection;
        private readonly IStringReplacementsService stringReplacementsService;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IViewComponentHelper viewComponentHelper;
        private readonly ITempDataProvider tempDataProvider;
        private readonly IActionContextAccessor actionContextAccessor;
        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly IObjectsService objectsService;
        private readonly ILanguagesService languagesService;
        private readonly IFiltersService filtersService;
        
        /// <summary>
        /// Initializes a new instance of <see cref="LegacyTemplatesService"/>.
        /// </summary>
        public TemplatesService(ILogger<LegacyTemplatesService> logger,
            IOptions<GclSettings> gclSettings,
            IDatabaseConnection databaseConnection,
            IStringReplacementsService stringReplacementsService,
            IHttpContextAccessor httpContextAccessor,
            IViewComponentHelper viewComponentHelper,
            ITempDataProvider tempDataProvider,
            IActionContextAccessor actionContextAccessor,
            IWebHostEnvironment webHostEnvironment,
            IFiltersService filtersService,
            IObjectsService objectsService,
            ILanguagesService languagesService)
        {
            this.gclSettings = gclSettings.Value;
            this.logger = logger;
            this.databaseConnection = databaseConnection;
            this.stringReplacementsService = stringReplacementsService;
            this.httpContextAccessor = httpContextAccessor;
            this.viewComponentHelper = viewComponentHelper;
            this.tempDataProvider = tempDataProvider;
            this.actionContextAccessor = actionContextAccessor;
            this.webHostEnvironment = webHostEnvironment;
            this.filtersService = filtersService;
            this.objectsService = objectsService;
            this.languagesService = languagesService;
        }

        /// <inheritdoc />
        public async Task<Template> GetTemplateAsync(int id = 0, string name = "", TemplateTypes type = TemplateTypes.Html, int parentId = 0, string parentName = "", bool includeContent = true)
        {
            if (id <= 0 && String.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException($"One of the parameters {nameof(id)} or {nameof(name)} must contain a value");
            }
            
            var joinPart = "";
            var whereClause = new List<string>();
            if (gclSettings.Environment == Environments.Development)
            {
                joinPart = $" JOIN (SELECT template_id, MAX(version) AS maxVersion FROM {WiserTableNames.WiserTemplate} GROUP BY template_id) AS maxVersion ON template.template_id = maxVersion.template_id AND template.version = maxVersion.maxVersion";
            }
            else
            {
                whereClause.Add($"(template.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}");
            }

            if (id > 0)
            {
                databaseConnection.AddParameter("id", id);
                whereClause.Add("template.template_id = ?id");
            }
            else
            {
                databaseConnection.AddParameter("name", name);
                whereClause.Add("template.template_name = ?name");
            }

            if (parentId > 0)
            {
                databaseConnection.AddParameter("parentId", parentId);
                whereClause.Add("template.parent_id = ?parentId");
            }
            else if (!String.IsNullOrWhiteSpace(parentName))
            {
                databaseConnection.AddParameter("parentName", parentName);
                whereClause.Add("parent1.template_name = ?parentName");
            }

            whereClause.Add("template.removed = 0");

            var query = $@"SELECT
                            IFNULL(parent5.template_name, IFNULL(parent4.template_name, IFNULL(parent3.template_name, IFNULL(parent2.template_name, parent1.template_name)))) as root_name, 
                            parent1.template_name AS parent_name, 
                            template.parent_id,
                            template.template_name,
                            template.template_type,
                            template.ordering,
                            parent1.ordering AS parent_ordering,
                            template.template_id,
                            GROUP_CONCAT(DISTINCT linkedCssTemplate.template_id) AS css_templates, 
                            GROUP_CONCAT(DISTINCT linkedJavascriptTemplate.template_id) AS javascript_templates,
                            template.load_always,
                            template.changed_on,
                            template.external_files,
                            {(includeContent ? "template.template_data_minified, template.template_data," : "")}
                            template.url_regex,
                            template.use_cache,
                            template.cache_minutes,
                            0 AS use_obfuscate,
                            template.insert_mode,
                            template.grouping_create_object_instead_of_array,
                            template.grouping_key_column_name,
                            template.grouping_value_column_name,
                            template.grouping_key,
                            template.grouping_prefix,
                            template.pre_load_query
                        FROM {WiserTableNames.WiserTemplate} AS template
                        {joinPart}
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent1 ON parent1.template_id = template.parent_id AND parent1.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = template.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent2 ON parent2.template_id = parent1.parent_id AND parent2.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent1.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent3 ON parent3.template_id = parent2.parent_id AND parent3.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent2.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent4 ON parent4.template_id = parent3.parent_id AND parent4.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent3.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent5 ON parent5.template_id = parent4.parent_id AND parent5.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent4.parent_id)

                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedCssTemplate ON FIND_IN_SET(linkedCssTemplate.template_id, template.linked_templates) AND linkedCssTemplate.template_type IN (2, 3) AND linkedCssTemplate.removed = 0
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedJavascriptTemplate ON FIND_IN_SET(linkedJavascriptTemplate.template_id, template.linked_templates) AND linkedJavascriptTemplate.template_type = 4 AND linkedJavascriptTemplate.removed = 0

                        WHERE {String.Join(" AND ", whereClause)}
                        GROUP BY template.template_id
                        ORDER BY parent5.ordering ASC, parent4.ordering ASC, parent3.ordering ASC, parent2.ordering ASC, parent1.ordering ASC, template.ordering ASC";

            await using var reader = await databaseConnection.GetReaderAsync(query);
            var result = await reader.ReadAsync() ? await reader.ToTemplateModelAsync(type) : new Template();

            return result;
        }

        /// <inheritdoc />
        public async Task<Template> GetTemplateCacheSettingsAsync(int id = 0, string name = "", int parentId = 0, string parentName = "")
        {
            if (id <= 0 && String.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException($"One of the parameters {nameof(id)} or {nameof(name)} must contain a value");
            }
            
            var joinPart = "";
            var whereClause = new List<string>();
            if (gclSettings.Environment == Environments.Development)
            {
                joinPart = $" JOIN (SELECT template_id, MAX(version) AS maxVersion FROM {WiserTableNames.WiserTemplate} GROUP BY template_id) AS maxVersion ON template.template_id = maxVersion.template_id AND template.version = maxVersion.maxVersion";
            }
            else
            {
                whereClause.Add($"(template.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}");
            }

            if (id > 0)
            {
                databaseConnection.AddParameter("id", id);
                whereClause.Add("template.template_id = ?id");
            }
            else
            {
                databaseConnection.AddParameter("name", name);
                whereClause.Add("template.template_name = ?name");
            }

            if (parentId > 0)
            {
                databaseConnection.AddParameter("parentId", parentId);
                whereClause.Add("template.parent_id = ?parentId");
            }
            else if (!String.IsNullOrWhiteSpace(parentName))
            {
                databaseConnection.AddParameter("parentName", parentName);
                whereClause.Add("parent1.template_name = ?parentName");
            }

            whereClause.Add("template.removed = 0");

            var query = $@"SELECT
                            template.template_name,
                            template.template_id,
                            template.use_cache,
                            template.cache_minutes
                        FROM {WiserTableNames.WiserTemplate} AS template
                        {joinPart}

                        WHERE {String.Join(" AND ", whereClause)}
                        GROUP BY template.template_id
                        LIMIT 1";

            var dataTable = await databaseConnection.GetAsync(query);
            var result = dataTable.Rows.Count == 0 ? new Template() : new Template
            {
                Id = dataTable.Rows[0].Field<int>("template_id"),
                Name = dataTable.Rows[0].Field<string>("template_name"),
                CachingMinutes = dataTable.Rows[0].Field<int>("cache_minutes"),
                CachingMode = dataTable.Rows[0].Field<TemplateCachingModes>("use_cache")
            };

            return result;
        }

        /// <inheritdoc />
        public async Task<DateTime?> GetGeneralTemplateLastChangedDateAsync(TemplateTypes templateType)
        {
            var joinPart = "";
            var whereClause = new List<string>();
            if (gclSettings.Environment == Environments.Development)
            {
                joinPart = $" JOIN (SELECT template_id, MAX(version) AS maxVersion FROM {WiserTableNames.WiserTemplate} GROUP BY template_id) AS maxVersion ON template.template_id = maxVersion.template_id AND template.version = maxVersion.maxVersion";
            }
            else
            {
                whereClause.Add($"(template.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}");
            }

            whereClause.Add("template.removed = 0");
            whereClause.Add("template.load_always = 1");
            whereClause.Add("template.template_type = ?templateType");

            var query = $@"SELECT MAX(template.changed_on) AS lastChanged
                        FROM {WiserTableNames.WiserTemplate} AS template
                        {joinPart}
                        WHERE {String.Join(" AND ", whereClause)}";

            databaseConnection.AddParameter("templateType", templateType);
            DateTime? result;
            await using var reader = await databaseConnection.GetReaderAsync(query);
            if (!await reader.ReadAsync())
            {
                return null;
            }

            var ordinal = reader.GetOrdinal("lastChanged");
            result = await reader.IsDBNullAsync(ordinal) ? null : reader.GetDateTime(ordinal);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<Template>> GetTemplatesAsync(ICollection<int> templateIds, bool includeContent)
        {
            var results = new List<Template>();
            databaseConnection.AddParameter("includeContent", includeContent);
            
            var joinPart = "";
            var whereClause = new List<string> { $"template.template_id IN ({String.Join(",", templateIds)})", "template.removed = 0" };
            if (gclSettings.Environment == Environments.Development)
            {
                joinPart = $" JOIN (SELECT template_id, MAX(version) AS maxVersion FROM {WiserTableNames.WiserTemplate} GROUP BY template_id) AS maxVersion ON template.template_id = maxVersion.template_id AND template.version = maxVersion.maxVersion";
            }
            else
            {
                whereClause.Add($"(template.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}");
            }

            var query = $@"SELECT
                            IFNULL(parent5.template_name, IFNULL(parent4.template_name, IFNULL(parent3.template_name, IFNULL(parent2.template_name, parent1.template_name)))) as root_name, 
                            parent1.template_name AS parent_name, 
                            template.parent_id,
                            template.template_name,
                            template.template_type,
                            template.ordering,
                            parent1.ordering AS parent_ordering,
                            template.template_id,
                            GROUP_CONCAT(DISTINCT linkedCssTemplate.template_id) AS css_templates, 
                            GROUP_CONCAT(DISTINCT linkedJavascriptTemplate.template_id) AS javascript_templates,
                            template.load_always,
                            template.changed_on,
                            template.external_files,
                            {(includeContent ? "template.template_data_minified, template.template_data," : "")}
                            template.url_regex,
                            template.use_cache,
                            template.cache_minutes,
                            0 AS use_obfuscate,
                            template.insert_mode,
                            template.grouping_create_object_instead_of_array,
                            template.grouping_key_column_name,
                            template.grouping_value_column_name,
                            template.grouping_key,
                            template.grouping_prefix,
                            template.pre_load_query
                        FROM {WiserTableNames.WiserTemplate} AS template
                        {joinPart}
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent1 ON parent1.template_id = template.parent_id AND parent1.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = template.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent2 ON parent2.template_id = parent1.parent_id AND parent2.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent1.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent3 ON parent3.template_id = parent2.parent_id AND parent3.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent2.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent4 ON parent4.template_id = parent3.parent_id AND parent4.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent3.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent5 ON parent5.template_id = parent4.parent_id AND parent5.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent4.parent_id)

                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedCssTemplate ON FIND_IN_SET(linkedCssTemplate.template_id, template.linked_templates) AND linkedCssTemplate.template_type IN (2, 3) AND linkedCssTemplate.removed = 0
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedJavascriptTemplate ON FIND_IN_SET(linkedJavascriptTemplate.template_id, template.linked_templates) AND linkedJavascriptTemplate.template_type = 4 AND linkedJavascriptTemplate.removed = 0

                        WHERE {String.Join(" AND ", whereClause)}
                        GROUP BY template.template_id
                        ORDER BY parent5.ordering ASC, parent4.ordering ASC, parent3.ordering ASC, parent2.ordering ASC, parent1.ordering ASC, template.ordering ASC";

            await using var reader = await databaseConnection.GetReaderAsync(query);
            while (await reader.ReadAsync())
            {
                var template = await reader.ToTemplateModelAsync();
                results.Add(template);
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<TemplateResponse> GetGeneralTemplateValueAsync(TemplateTypes templateType)
        {
            var joinPart = "";
            var whereClause = new List<string>();
            if (gclSettings.Environment == Environments.Development)
            {
                joinPart = $" JOIN (SELECT template_id, MAX(version) AS maxVersion FROM {WiserTableNames.WiserTemplate} GROUP BY template_id) AS maxVersion ON template.template_id = maxVersion.template_id AND template.version = maxVersion.maxVersion";
            }
            else
            {
                whereClause.Add($"(template.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}");
            }

            whereClause.Add("template.removed = 0");
            whereClause.Add("template.load_always = 1");

            whereClause.Add(templateType is TemplateTypes.Css or TemplateTypes.Scss 
                ? $"template.template_type IN ({(int)TemplateTypes.Css}, {(int)TemplateTypes.Scss})" 
                : $"template.template_type = {(int)templateType}");

            var query = $@"SELECT
                            IFNULL(parent5.template_name, IFNULL(parent4.template_name, IFNULL(parent3.template_name, IFNULL(parent2.template_name, parent1.template_name)))) as root_name, 
                            parent1.template_name AS parent_name, 
                            template.parent_id,
                            template.template_name,
                            template.template_type,
                            template.ordering,
                            parent1.ordering AS parent_ordering,
                            template.template_id,
                            GROUP_CONCAT(DISTINCT linkedCssTemplate.template_id) AS css_templates, 
                            GROUP_CONCAT(DISTINCT linkedJavascriptTemplate.template_id) AS javascript_templates,
                            template.load_always,
                            template.changed_on,
                            template.external_files,
                            template.template_data_minified,
                            template.template_data,
                            template.url_regex,
                            template.use_cache,
                            template.cache_minutes,
                            0 AS use_obfuscate,
                            template.insert_mode,
                            template.grouping_create_object_instead_of_array,
                            template.grouping_key_column_name,
                            template.grouping_value_column_name,
                            template.grouping_key,
                            template.grouping_prefix
                        FROM {WiserTableNames.WiserTemplate} AS template
                        {joinPart}
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent1 ON parent1.template_id = template.parent_id AND parent1.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = template.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent2 ON parent2.template_id = parent1.parent_id AND parent2.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent1.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent3 ON parent3.template_id = parent2.parent_id AND parent3.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent2.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent4 ON parent4.template_id = parent3.parent_id AND parent4.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent3.parent_id)
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS parent5 ON parent5.template_id = parent4.parent_id AND parent5.version = (SELECT MAX(version) FROM {WiserTableNames.WiserTemplate} WHERE template_id = parent4.parent_id)

                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedCssTemplate ON FIND_IN_SET(linkedCssTemplate.template_id, template.linked_templates) AND linkedCssTemplate.template_type IN (2, 3) AND linkedCssTemplate.removed = 0
                        LEFT JOIN {WiserTableNames.WiserTemplate} AS linkedJavascriptTemplate ON FIND_IN_SET(linkedJavascriptTemplate.template_id, template.linked_templates) AND linkedJavascriptTemplate.template_type = 4 AND linkedJavascriptTemplate.removed = 0

                        WHERE {String.Join(" AND ", whereClause)}
                        GROUP BY template.template_id
                        ORDER BY parent5.ordering ASC, parent4.ordering ASC, parent3.ordering ASC, parent2.ordering ASC, parent1.ordering ASC, template.ordering ASC";

            var result = new TemplateResponse();
            var resultBuilder = new StringBuilder();
            var idsLoaded = new List<int>();
            var currentUrl = HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext).ToString();

            await using var reader = await databaseConnection.GetReaderAsync(query);
            while (await reader.ReadAsync())
            {
                var template = await reader.ToTemplateModelAsync();
                await AddTemplateToResponseAsync(idsLoaded, template, currentUrl, resultBuilder, result);
            }

            result.Content = resultBuilder.ToString();

            if (result.LastChangeDate == DateTime.MinValue)
            {
                result.LastChangeDate = DateTime.Now;
            }

            if (templateType is TemplateTypes.Css or TemplateTypes.Scss)
            {
                result.Content = CssHelpers.MoveImportStatementsToTop(result.Content);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<TemplateResponse> GetCombinedTemplateValueAsync(ICollection<int> templateIds, TemplateTypes templateType)
        {
            return await GetCombinedTemplateValueAsync(this, templateIds, templateType);
        }

        /// <inheritdoc />
        public async Task<TemplateResponse> GetCombinedTemplateValueAsync(ITemplatesService templatesService, ICollection<int> templateIds, TemplateTypes templateType)
        {
            var result = new TemplateResponse();
            var resultBuilder = new StringBuilder();
            var idsLoaded = new List<int>();
            var currentUrl = HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext).ToString();
            var templates = await templatesService.GetTemplatesAsync(templateIds, true);

            foreach (var template in templates.Where(t => t.Type == templateType))
            {
                await templatesService.AddTemplateToResponseAsync(idsLoaded, template, currentUrl, resultBuilder, result);
            }

            result.Content = resultBuilder.ToString();

            if (result.LastChangeDate == DateTime.MinValue)
            {
                result.LastChangeDate = DateTime.Now;
            }

            if (templateType == TemplateTypes.Css)
            {
                result.Content = CssHelpers.MoveImportStatementsToTop(result.Content);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task AddTemplateToResponseAsync(ICollection<int> idsLoaded, Template template, string currentUrl, StringBuilder resultBuilder, TemplateResponse templateResponse)
        {
            if (idsLoaded.Contains(template.Id))
            {
                // Make sure that we don't add the same template twice.
                return;
            }

            if (!String.IsNullOrWhiteSpace(template.UrlRegex) && !Regex.IsMatch(currentUrl, template.UrlRegex))
            {
                // Skip this template if it has an URL regex and that regex does not match the current URL.
                return;
            }

            idsLoaded.Add(template.Id);

            // Get files from Wiser CDN.
            if (template.WiserCdnFiles.Any())
            {
                resultBuilder.AppendLine(await GetWiserCdnFilesAsync(template.WiserCdnFiles));
            }

            // Get the template contents.
            resultBuilder.AppendLine(template.Content);

            // Get the change date.
            if (template.LastChanged > templateResponse.LastChangeDate)
            {
                templateResponse.LastChangeDate = template.LastChanged;
            }

            // Get any external files that we need to load.
            templateResponse.ExternalFiles.AddRange(template.ExternalFiles);
        }

        /// <inheritdoc />
        public async Task<string> GetWiserCdnFilesAsync(ICollection<string> fileNames)
        {
            if (fileNames == null)
            {
                throw new ArgumentNullException(nameof(fileNames));
            }

            var enumerable = fileNames.ToList();
            if (!enumerable.Any())
            {
                return "";
            }

            var resultBuilder = new StringBuilder();
            using var webClient = new WebClient();
            foreach (var fileName in enumerable.Where(fileName => !String.IsNullOrWhiteSpace(fileName)))
            {
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                var directory = extension switch
                {
                    ".js" => "scripts",
                    _ => extension.Substring(1)
                };

                var localDirectory = Path.Combine(webHostEnvironment.WebRootPath, directory);
                if (!Directory.Exists(localDirectory))
                {
                    Directory.CreateDirectory(localDirectory);
                }

                var fileLocation = Path.Combine(localDirectory, fileName);
                if (!File.Exists(fileLocation))
                {
                    await webClient.DownloadFileTaskAsync(new Uri($"https://app.wiser.nl/{directory}/cdn/{fileName}"), fileLocation);
                }

                resultBuilder.AppendLine(await File.ReadAllTextAsync(fileLocation));
            }

            return resultBuilder.ToString();
        }

        /// <inheritdoc />
        public async Task<string> DoReplacesAsync(string input, bool handleStringReplacements = true, bool handleDynamicContent = true, bool evaluateLogicSnippets = true, DataRow dataRow = null, bool handleRequest = true, bool removeUnknownVariables = true, bool forQuery = false)
        {
            return await DoReplacesAsync(this, input, handleStringReplacements, handleDynamicContent, evaluateLogicSnippets, dataRow, handleRequest, removeUnknownVariables, forQuery);
        }

        /// <inheritdoc />
        public async Task<string> DoReplacesAsync(ITemplatesService templatesService, string input, bool handleStringReplacements = true, bool handleDynamicContent = true, bool evaluateLogicSnippets = true, DataRow dataRow = null, bool handleRequest = true, bool removeUnknownVariables = true, bool forQuery = false)
        {
            // Input cannot be empty.
            if (String.IsNullOrEmpty(input))
            {
                return input;
            }

            // Start with special template replacements for the pre load query that you can set in HTML templates in the templates module in Wiser.
            if (httpContextAccessor.HttpContext != null && httpContextAccessor.HttpContext.Items.ContainsKey(Constants.TemplatePreLoadQueryResultKey))
            {
                input = stringReplacementsService.DoReplacements(input, (DataRow)httpContextAccessor.HttpContext.Items[Constants.TemplatePreLoadQueryResultKey], forQuery, prefix: "{template.");
            }

            // Then do the normal string replacements, because includes can contain variables in a query string, which need to be replaced first.
            if (handleStringReplacements)
            {
                input = await stringReplacementsService.DoAllReplacementsAsync(input, dataRow, handleRequest, false, removeUnknownVariables, forQuery);
            }

            // HTML and mail templates.
            // Note: The string replacements service cannot handle the replacing of templates, because that would cause the StringReplacementsService to need
            // the TemplatesService, which in turn needs the StringReplacementsService, creating a circular dependency.
            input = await templatesService.HandleIncludesAsync(input, forQuery: forQuery);
            input = await templatesService.HandleImageTemplating(input);

            // Replace dynamic content.
            if (handleDynamicContent && !forQuery)
            {
                input = await templatesService.ReplaceAllDynamicContentAsync(input);
            }

            if (evaluateLogicSnippets)
            {
                input = stringReplacementsService.EvaluateTemplate(input);
            }

            return input;
        }
        
        /// <inheritdoc />
        public async Task<string> GenerateImageUrl(string itemId, string type, int number, string filename = "", string width = "0", string height = "0", string resizeMode = "")
        {
            var imageUrlTemplate = await objectsService.FindSystemObjectByDomainNameAsync("image_url_template", "/image/wiser2/<item_id>/<type>/<resizemode>/<width>/<height>/<number>/<filename>");

            imageUrlTemplate = imageUrlTemplate.Replace("<item_id>", itemId);
            imageUrlTemplate = imageUrlTemplate.Replace("<filename>", filename);
            imageUrlTemplate = imageUrlTemplate.Replace("<type>", type);
            imageUrlTemplate = imageUrlTemplate.Replace("<width>", width);
            imageUrlTemplate = imageUrlTemplate.Replace("<height>", height);

            // Remove if not specified
            if (number == 0)
            {
                imageUrlTemplate = imageUrlTemplate.Replace("<number>/", "");
            }

            // Remove if not specified
            if (String.IsNullOrWhiteSpace(resizeMode))
            {
                imageUrlTemplate = imageUrlTemplate.Replace("<resizemode>/", "");
            }

            imageUrlTemplate = imageUrlTemplate.Replace("<number>", number.ToString());
            imageUrlTemplate = imageUrlTemplate.Replace("<resizemode>", resizeMode);

            return imageUrlTemplate;
        }
        
        /// <inheritdoc />
        public async Task<string> HandleImageTemplating(string input)
        {
            if (String.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            var imageTemplatingRegex = new Regex(@"\[image\[(.*?)\]\]");
            foreach (Match m in imageTemplatingRegex.Matches(input))
            {
                var replacementParameters = m.Groups[1].Value.Split(":");
                var outputBuilder = new StringBuilder();
                var imageIndex = 0;
                var resizeMode = "";
                var propertyName = "";
                var imageAltTag = "";
                var parameters = replacementParameters[0].Split(",");
                var imageItemIdOrFilename = parameters[0];

                // Only get the parameter if specified in the templating variable
                if (parameters.Length > 1)
                {
                    propertyName = parameters[1].Trim();
                }

                if (parameters.Length > 2)
                {
                    imageIndex = Int32.Parse(parameters[2].Trim());
                }

                if (parameters.Length > 3)
                {
                    resizeMode = parameters[3].Trim();
                }

                if (parameters.Length > 4)
                {
                    imageAltTag = parameters[4].Trim();
                }

                imageIndex = imageIndex == 0 ? 1 : imageIndex;
                 
                // Get the image from the database
                databaseConnection.AddParameter("itemId", imageItemIdOrFilename);
                databaseConnection.AddParameter("filename", imageItemIdOrFilename);
                databaseConnection.AddParameter("propertyName", propertyName);

                var queryWherePart = Char.IsNumber(imageItemIdOrFilename, 0) ? "item_id = ?itemId" : "file_name = ?filename";
                var dataTable = await databaseConnection.GetAsync(@$"SELECT * FROM `{WiserTableNames.WiserItemFile}` WHERE {queryWherePart} AND IF(?propertyName = '', 1=1, property_name = ?propertyName) AND content_type LIKE 'image%' ORDER BY id ASC");

                if (dataTable.Rows.Count == 0)
                {
                    input = input.ReplaceCaseInsensitive(m.Value, "image not found");
                    continue;
                }

                if (imageIndex > dataTable.Rows.Count)
                {
                    input = input.ReplaceCaseInsensitive(m.Value, "specified image index out of bound");
                    continue;
                }

                // Get various values from the table
                var imageItemId = dataTable.Rows[imageIndex-1].Field<int>("item_id").ToString();
                var imageFilename = dataTable.Rows[imageIndex-1].Field<string>("file_name");
                var imagePropertyType = dataTable.Rows[imageIndex-1].Field<string>("property_name");
                var imageFilenameWithoutExt = Path.GetFileNameWithoutExtension(imageFilename);
                var imageTemplatingSetsRegex = new Regex(@"\:(.*?)\)");
                var items = imageTemplatingSetsRegex.Matches(m.Groups[1].Value);
                var totalItems = items.Count;
                var index = 1;

                if (items.Count == 0)
                {
                    input = input.ReplaceCaseInsensitive(m.Value, "no image set(s) specified, you must at least specify one set");
                    continue;
                }

                foreach (Match s in items)
                {
                    var imageTemplate = await objectsService.FindSystemObjectByDomainNameAsync("image_template", "<figure><picture>{images}</picture></figure>");

                    // Get the specified parameters from the regex match
                    parameters = s.Value.Split(":")[1].Split("(");
                    var imageParameters = parameters[1].Replace(")", "").Split("x");
                    var imageViewportParameter = parameters[0];

                    if (String.IsNullOrWhiteSpace(imageViewportParameter))
                    {
                        input = input.ReplaceCaseInsensitive(m.Value, "no viewport parameter specified");
                        continue;
                    }

                    var imageWidth = Convert.ToInt32(imageParameters[0]);
                    var imageHeight = Convert.ToInt32(imageParameters[1]);
                    var imageWidth2X = (imageWidth * 2).ToString();
                    var imageHeight2X = (imageHeight * 2).ToString();

                    outputBuilder.Append(@"<source media=""(min-width: {min-width}px)"" srcset=""{image-url-webp-2x} 2x, {image-url-webp}"" type=""image/webp"" />");
                    outputBuilder.Append(@"<source media=""(min-width: {min-width}px)"" srcset=""{image-url-jpg-2x} 2x, {image-url-jpg}"" type=""image/jpeg"" />");

                    outputBuilder.Replace("{image-url-webp}", await GenerateImageUrl(imageItemId, imagePropertyType, imageIndex, imageFilenameWithoutExt + ".webp", imageWidth.ToString(), imageHeight.ToString(), resizeMode));
                    outputBuilder.Replace("{image-url-jpg}", await GenerateImageUrl(imageItemId, imagePropertyType, imageIndex, imageFilenameWithoutExt + ".jpg", imageWidth.ToString(), imageHeight.ToString(), resizeMode));
                    outputBuilder.Replace("{image-url-webp-2x}", await GenerateImageUrl(imageItemId, imagePropertyType, imageIndex, imageFilenameWithoutExt + ".webp", imageWidth2X, imageHeight2X, resizeMode));
                    outputBuilder.Replace("{image-url-jpg-2x}", await GenerateImageUrl(imageItemId, imagePropertyType, imageIndex, imageFilenameWithoutExt + ".jpg", imageWidth2X, imageHeight2X, resizeMode));
                    outputBuilder.Replace("{min-width}", imageViewportParameter);

                    // If last item, than add the default image
                    if (index == totalItems)
                    {
                        outputBuilder.Append("<img width=\"100%\" height=\"auto\" loading=\"lazy\" src=\"{default_image_link}\" alt=\"{image_alt}\">");
                        outputBuilder.Replace("{default_image_link}", await GenerateImageUrl(imageItemId, imagePropertyType, imageIndex, imageFilenameWithoutExt + ".webp", imageWidth.ToString(), imageHeight.ToString(), resizeMode));
                    }

                    imageTemplate = imageTemplate.Replace("{images}", outputBuilder.ToString());
                    imageTemplate = imageTemplate.Replace("{image_alt}", (String.IsNullOrWhiteSpace(imageAltTag) ? imageFilename : imageAltTag));

                    // Replace the image in the template
                    input = input.ReplaceCaseInsensitive(m.Value, imageTemplate);

                    index += 1;
                }
            }

            return input;
        }

        /// <inheritdoc />
        public async Task<string> HandleIncludesAsync(string input, bool handleStringReplacements = true, DataRow dataRow = null, bool handleRequest = true, bool forQuery = false)
        {
            return await HandleIncludesAsync(this, input, handleStringReplacements, dataRow, handleRequest, forQuery);
        }

        /// <inheritdoc />
        public async Task<string> HandleIncludesAsync(ITemplatesService templatesService, string input, bool handleStringReplacements = true, DataRow dataRow = null, bool handleRequest = true, bool forQuery = false)
        {
            if (String.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            const int max = 10;
            var counter = 0;

            // We use a while loop here because it's possible to to include a template that has another include, so we might have to replace them multiple times.
            while (counter < max && (input.Contains("<[", StringComparison.Ordinal) || input.Contains("[include", StringComparison.Ordinal)))
            {
                counter += 1;
                var inclusionsRegex = new Regex(@"<\[(.*?)\]>");
                foreach (Match m in inclusionsRegex.Matches(input))
                {
                    var templateName = m.Groups[1].Value;
                    if (templateName.Contains("{"))
                    {
                        // Make sure replaces for the template name are done
                        templateName = await stringReplacementsService.DoAllReplacementsAsync(templateName, dataRow, handleRequest, forQuery: forQuery);
                    }

                    // Replace templates (syntax is <[templateName]> or <[parentFolder\templateName]>
                    if (templateName.Contains("\\"))
                    {
                        // Contains a parent
                        var split = templateName.Split('\\');
                        var template = await templatesService.GetTemplateAsync(name: split[1], parentName: split[0]);
                        if (handleStringReplacements)
                        {
                            template.Content = await stringReplacementsService.DoAllReplacementsAsync(template.Content, dataRow, handleRequest, false, false, forQuery);
                        }

                        input = input.ReplaceCaseInsensitive(m.Groups[0].Value, template.Content);
                    }
                    else
                    {
                        var template = await templatesService.GetTemplateAsync(name: templateName);
                        if (handleStringReplacements)
                        {
                            template.Content = await stringReplacementsService.DoAllReplacementsAsync(template.Content, dataRow, handleRequest, false, false, forQuery);
                        }

                        input = input.ReplaceCaseInsensitive(m.Groups[0].Value, template.Content);
                    }
                }

                inclusionsRegex = new Regex(@"\[include\[([^{?\]]*)(\?)?([^{?\]]*?)\]\]");
                foreach (Match m in inclusionsRegex.Matches(input))
                {
                    var templateName = m.Groups[1].Value;
                    var queryString = m.Groups[3].Value.Replace("&amp;", "&");
                    if (templateName.Contains("{"))
                    {
                        // Make sure replaces for the template name are done
                        templateName = await stringReplacementsService.DoAllReplacementsAsync(templateName, dataRow, handleRequest, forQuery: forQuery);
                    }

                    // Replace templates (syntax is [include[templateName]] or [include[parentFolder\templateName]] or [include[templateName?x=y]]
                    if (templateName.Contains("\\"))
                    {
                        // Contains a parent
                        var split = templateName.Split('\\');
                        var template = await templatesService.GetTemplateAsync(name: split[1], parentName: split[0]);
                        var values = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => new KeyValuePair<string, string>(x.Split('=')[0], x.Split('=')[1]));
                        var content = stringReplacementsService.DoReplacements(template.Content, values, forQuery);
                        if (handleStringReplacements)
                        {
                            content = await stringReplacementsService.DoAllReplacementsAsync(content, dataRow, handleRequest, false, false, forQuery);
                        }

                        if (!String.IsNullOrWhiteSpace(queryString))
                        {
                            content = content.Replace("<div class=\"dynamic-content", $"<div data=\"{queryString}\" class=\"/dynamic-content");
                        }

                        input = input.ReplaceCaseInsensitive(m.Groups[0].Value, content);
                    }
                    else
                    {
                        var template = await templatesService.GetTemplateAsync(name: templateName);
                        var values = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => new KeyValuePair<string, string>(x.Split('=')[0], x.Split('=')[1]));
                        var content = stringReplacementsService.DoReplacements(template.Content, values, forQuery);
                        if (handleStringReplacements)
                        {
                            content = await stringReplacementsService.DoAllReplacementsAsync(content, dataRow, handleRequest, false, false, forQuery);
                        }

                        if (!String.IsNullOrWhiteSpace(queryString))
                        {
                            content = content.Replace("<div class=\"dynamic-content", $"<div data=\"{queryString}\" class=\"/dynamic-content");
                        }

                        input = input.ReplaceCaseInsensitive(m.Groups[0].Value, content);
                    }
                }
            }

            return input;
        }

        /// <inheritdoc />
        public async Task<DynamicContent> GetDynamicContentData(int contentId)
        {
            var query = gclSettings.Environment == Environments.Development 
                ? @$"SELECT 
                    component.content_id,
                    component.settings,
                    component.component,
                    component.component_mode,
                    component.version
                FROM {WiserTableNames.WiserDynamicContent} AS component
                LEFT JOIN {WiserTableNames.WiserDynamicContent} AS otherVersion ON otherVersion.content_id = component.content_id AND otherVersion.version > component.version
                WHERE component.content_id = ?contentId
                AND otherVersion.id IS NULL" 
                : @$"SELECT 
                    component.content_id,
                    component.settings,
                    component.component,
                    component.component_mode,
                    component.version
                FROM {WiserTableNames.WiserDynamicContent} AS component
                WHERE component.content_id = ?contentId
                AND (component.published_environment & {(int)gclSettings.Environment}) = {(int)gclSettings.Environment}
                ORDER BY component.version DESC
                LIMIT 1";
            
            databaseConnection.AddParameter("contentId", contentId);
            var dataTable = await databaseConnection.GetAsync(query);
            if (dataTable.Rows.Count == 0)
            {
                return null;
            }

            return new DynamicContent
            {
                Id = contentId,
                Name = dataTable.Rows[0].Field<string>("component"),
                SettingsJson = dataTable.Rows[0].Field<string>("settings"),
                ComponentMode = dataTable.Rows[0].Field<string>("component_mode"),
                Version = dataTable.Rows[0].Field<int>("version")
            };
        }

        /// <inheritdoc />
        public async Task<(object result, ViewDataDictionary viewData)> GenerateDynamicContentHtmlAsync(DynamicContent dynamicContent, int? forcedComponentMode = null, string callMethod = null, Dictionary<string, string> extraData = null)
        {
            if (String.IsNullOrWhiteSpace(dynamicContent?.Name) || String.IsNullOrWhiteSpace(dynamicContent?.SettingsJson))
            {
                return ("", null);
            }

            var viewComponentName = dynamicContent.Name;
            
            // Create a fake ViewContext (but with a real ActionContext and a real HttpContext).
            var viewContext = new ViewContext(
                actionContextAccessor.ActionContext,
                NullView.Instance,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
                new TempDataDictionary(httpContextAccessor.HttpContext, tempDataProvider),
                TextWriter.Null,
                new HtmlHelperOptions());

            // Set the context in the ViewComponentHelper, so that the ViewComponents that we use actually have the proper context.
            (viewComponentHelper as IViewContextAware)?.Contextualize(viewContext);

            // Dynamically invoke the correct ViewComponent.
            var component = await viewComponentHelper.InvokeAsync(viewComponentName, new { dynamicContent, callMethod, forcedComponentMode, extraData });

            // If there is a InvokeMethodResult, it means this that a specific method on a specific component was called via /gclcomponent.gcl
            // and we only want to return the results of that method, instead of rendering the entire component.
            if (viewContext.TempData.ContainsKey("InvokeMethodResult") && viewContext.TempData["InvokeMethodResult"] != null)
            {
                return (viewContext.TempData["InvokeMethodResult"], viewContext.ViewData);
            }

            await using var stringWriter = new StringWriter();
            component.WriteTo(stringWriter, HtmlEncoder.Default);
            var html = stringWriter.ToString();

            return (html, viewContext.ViewData);
        }

        /// <inheritdoc />
        public async Task<(object result, ViewDataDictionary viewData)> GenerateDynamicContentHtmlAsync(int componentId, int? forcedComponentMode = null, string callMethod = null, Dictionary<string, string> extraData = null)
        {
            var dynamicContent = await GetDynamicContentData(componentId);
            return await GenerateDynamicContentHtmlAsync(dynamicContent, forcedComponentMode, callMethod, extraData);
        }

        /// <inheritdoc />
        public async Task<string> ReplaceAllDynamicContentAsync(string template, List<DynamicContent> componentOverrides = null)
        {
            return await ReplaceAllDynamicContentAsync(this, template, componentOverrides);
        }

        /// <inheritdoc />
        public async Task<string> ReplaceAllDynamicContentAsync(ITemplatesService templatesService, string template, List<DynamicContent> componentOverrides = null)
        {
            if (String.IsNullOrWhiteSpace(template))
            {
                return template;
            }
            
            // Timeout on the regular expression to prevent denial of service attacks.
            var regEx = new Regex(@"<div[^<>]*?(?:class=['""]dynamic-content['""][^<>]*?)?(?:data=['""](?<data>.*?)['""][^>]*?)?(component-id|content-id)=['""](?<contentId>\d+)['""][^>]*?>[^<>]*?<h2>[^<>]*?(?<title>[^<>]*?)<\/h2>[^<>]*?<\/div>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMinutes(3));

            var matches = regEx.Matches(template);
            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                if (!Int32.TryParse(match.Groups["contentId"].Value, out var contentId) || contentId <= 0)
                {
                    logger.LogWarning($"Found dynamic content with invalid componentId of '{match.Groups["contentId"].Value}', so ignoring it.");
                    continue;
                }

                try
                {
                    var extraData = match.Groups["data"].Value?.ToDictionary("&", "=");
                    var dynamicContentData = componentOverrides?.FirstOrDefault(d => d.Id == contentId);
                    var (html, _) = dynamicContentData == null ? await templatesService.GenerateDynamicContentHtmlAsync(contentId, extraData: extraData) : await templatesService.GenerateDynamicContentHtmlAsync(dynamicContentData, extraData: extraData);
                    template = template.Replace(match.Value, (string)html);
                }
                catch (Exception exception)
                {
                    logger.LogError($"An error while generating component with id '{contentId}': {exception}");
                    var errorOnPage = $"An error occurred while generating component with id '{contentId}'";
                    if (gclSettings.Environment is Environments.Development or Environments.Test)
                    {
                        errorOnPage += $": {exception.Message}";
                    }

                    template = template.Replace(match.Value, errorOnPage);
                }
            }

            return template;
        }

        /// <inheritdoc />
        public async Task<JArray> GetJsonResponseFromQueryAsync(QueryTemplate queryTemplate, string encryptionKey = null, bool skipNullValues = false, bool allowValueDecryption = false, bool recursive = false)
        {
            var query = queryTemplate?.Content;
            if (String.IsNullOrWhiteSpace(query))
            {
                return null;
            }
            
            queryTemplate.GroupingSettings ??= new QueryGroupingSettings();
            query = await DoReplacesAsync(query, true, false, true, null, true, false, true);
            if (query.Contains("{filters}", StringComparison.OrdinalIgnoreCase))
            {
                query = query.ReplaceCaseInsensitive("{filters}", (await filtersService.GetFilterQueryPartAsync()).JoinPart);
            }

            var pusherRegex = new Regex(@"PUSHER<channel\((.*?)\),event\((.*?)\),message\(((?s:.)*?)\)>", RegexOptions.Compiled);
            var pusherMatches = pusherRegex.Matches(query);
            foreach (Match match in pusherMatches)
            {
                query = query.Replace(match.Value, "");
            }

            if (recursive)
            {
                queryTemplate.GroupingSettings.GroupingColumn = "id";
            }

            var dataTable = await databaseConnection.GetAsync(query);
            var result = dataTable.Rows.Count == 0 ? new JArray() : dataTable.ToJsonArray(queryTemplate.GroupingSettings, encryptionKey, skipNullValues, allowValueDecryption, recursive);

            if (pusherMatches.Any())
            {
                throw new NotImplementedException("Pusher messages not yet implemented");
            }

            return result;
        }
        
        /// <inheritdoc />
        public async Task<TemplateDataModel> GetTemplateDataAsync(int id = 0, string name = "", int parentId = 0, string parentName = "")
        {
            return await GetTemplateDataAsync(this, id, name, parentId, parentName);
        }
        
        /// <inheritdoc />
        public async Task<TemplateDataModel> GetTemplateDataAsync(ITemplatesService templatesService, int id = 0, string name = "", int parentId = 0, string parentName = "")
        {
            var template = await templatesService.GetTemplateAsync(id, name, TemplateTypes.Html, parentId, parentName);

            var cssStringBuilder = new StringBuilder();
            var jsStringBuilder = new StringBuilder();
            foreach (var templateId in template.CssTemplates.Concat(template.JavascriptTemplates))
            {
                var linkedTemplate = await templatesService.GetTemplateAsync(templateId);
                (linkedTemplate.Type == TemplateTypes.Css ? cssStringBuilder : jsStringBuilder).Append(linkedTemplate.Content);
            }

            return new TemplateDataModel
            {
                Content = template.Content, 
                LinkedCss = cssStringBuilder.ToString(), 
                LinkedJavascript = jsStringBuilder.ToString()
            }; 
        }

        /// <inheritdoc />
        public async Task ExecutePreLoadQueryAndRememberResultsAsync(Template template)
        {
            await ExecutePreLoadQueryAndRememberResultsAsync(this, template);
        }

        /// <inheritdoc />
        public async Task ExecutePreLoadQueryAndRememberResultsAsync(ITemplatesService templatesService, Template template)
        {
            if (httpContextAccessor.HttpContext == null || String.IsNullOrWhiteSpace(template?.PreLoadQuery))
            {
                return;
            }

            var query = await DoReplacesAsync(templatesService, template.PreLoadQuery, forQuery: true);
            var dataTable = await databaseConnection.GetAsync(query);
            if (dataTable.Rows.Count == 0)
            {
                return;
            }

            httpContextAccessor.HttpContext.Items.Add(Constants.TemplatePreLoadQueryResultKey, dataTable.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<string> GetTemplateOutputCacheFileNameAsync(Template contentTemplate)
        {
            var originalUri = HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext);
            var cacheFileName = new StringBuilder($"template_{contentTemplate.Id}_");
            switch (contentTemplate.CachingMode)
            {
                case TemplateCachingModes.ServerSideCaching:
                    break;
                case TemplateCachingModes.ServerSideCachingPerUrl:
                    cacheFileName.Append(Uri.EscapeDataString(originalUri.AbsolutePath.ToSha512Simple()));
                    break;
                case TemplateCachingModes.ServerSideCachingPerUrlAndQueryString:
                    cacheFileName.Append(Uri.EscapeDataString(originalUri.PathAndQuery.ToSha512Simple()));
                    break;
                case TemplateCachingModes.ServerSideCachingPerHostNameAndQueryString:
                    cacheFileName.Append(Uri.EscapeDataString(originalUri.ToString().ToSha512Simple()));
                    break;
                case TemplateCachingModes.NoCaching:
                    return "";
                default:
                    throw new ArgumentOutOfRangeException(nameof(contentTemplate.CachingMode), contentTemplate.CachingMode.ToString());
            }

            // If the caching should deviate based on certain cookies, then the names and values of those cookies should be added to the file name.
            var cookieCacheDeviation = (await objectsService.FindSystemObjectByDomainNameAsync("contentcaching_cookie_deviation")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (cookieCacheDeviation.Length > 0)
            {
                var requestCookies = httpContextAccessor.HttpContext?.Request.Cookies;
                foreach (var cookieName in cookieCacheDeviation)
                {
                    if (requestCookies == null || !requestCookies.TryGetValue(cookieName, out var cookieValue))
                    {
                        continue;
                    }

                    var combinedCookiePart = $"{cookieName}:{cookieValue}";
                    cacheFileName.Append($"_{Uri.EscapeDataString(combinedCookiePart.ToSha512Simple())}");
                }
            }

            // And finally add the language code to the file name.
            if (!String.IsNullOrWhiteSpace(languagesService.CurrentLanguageCode))
            {
                cacheFileName.Append($"_{languagesService.CurrentLanguageCode}");
            }

            cacheFileName.Append(".html");

            return cacheFileName.ToString();
        }

        /// <summary>
        /// Do all replacement which have to do with request, session or cookie.
        /// Only use this function if you can't add ITemplatesService via dependency injection, otherwise you should use the non static functions <see cref="IStringReplacementsService.DoSessionReplacements" /> and <see cref="IStringReplacementsService.DoHttpRequestReplacements"/>.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="httpContext"></param>
        public static string DoHttpContextReplacements(string input, HttpContext httpContext)
        {
            // Querystring replaces.
            foreach (var key in httpContext.Request.Query.Keys)
            {
                input = input.ReplaceCaseInsensitive($"{{{key}}}", httpContext.Request.Query[key]);
            }

            // Form replaces.
            if (httpContext.Request.HasFormContentType)
            {
                foreach (var variable in httpContext.Request.Form.Keys)
                {
                    input = input.ReplaceCaseInsensitive($"{{{variable}}}", httpContext.Request.Form[variable]);
                }
            }

            // Session replaces.
            if (httpContext?.Features.Get<ISessionFeature>() != null && httpContext.Session.IsAvailable)
            {
                foreach (var variable in httpContext.Session.Keys)
                {
                    input = input.ReplaceCaseInsensitive($"{{{variable}}}", httpContext.Session.GetString(variable));
                }
            }

            // Cookie replaces.
            foreach (var key in httpContext.Request.Cookies.Keys)
            {
                input = input.ReplaceCaseInsensitive($"{{{key}}}", httpContext.Request.Cookies[key]);
            }

            return input;
        }
    }
}
