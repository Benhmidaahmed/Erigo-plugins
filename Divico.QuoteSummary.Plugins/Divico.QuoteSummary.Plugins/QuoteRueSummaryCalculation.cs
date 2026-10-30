using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Divico.Sales.Plugins
{
    /// <summary>
    /// Reconstruit les lignes de synthèse d'un devis par rue/zone.
    ///
    /// Enregistrement recommandé :
    /// - Table : quotedetail
    /// - Messages : Create, Update, Delete
    /// - Stage : PostOperation
    /// - Mode : Synchronous
    ///
    /// Pour Update/Delete, enregistrer une PreImage nommée "PreImage"
    /// contenant le champ quoteid.
    /// </summary>
    public class QuoteRueSummaryCalculation : IPlugin
    {
        private const string EntityQuote = "quote";
        private const string EntityQuoteDetail = "quotedetail";
        private const string EntitySummary = "bsc_quotesummarybyrue";

        private const string QuoteDetailQuote = "quoteid";
        private const string QuoteDetailGrouping = "bsc_concernant";
        private const string QuoteDetailBaseAmount = "baseamount";
        private const string QuoteDetailTax = "tax";
        private const string QuoteDetailExtendedAmount = "extendedamount";

        private const string SummaryPrimaryId = "bsc_quotesummarybyrueid";
        private const string SummaryName = "bsc_name";
        private const string SummaryQuote = "bsc_devis";
        private const string SummaryGrouping = "bsc_concernant";
        private const string SummaryHt = "bsc_montantht";
        private const string SummaryTax = "bsc_montanttaxe";
        private const string SummaryTtc = "bsc_montantttc";

        private const string PreImageName = "PreImage";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                tracing.Trace(
                    $"QuoteRueSummaryCalculation: début. " +
                    $"Entité={context.PrimaryEntityName}, Message={context.MessageName}, " +
                    $"Depth={context.Depth}");

                if (context.Depth > 1)
                {
                    tracing.Trace("QuoteRueSummaryCalculation: arrêt anti-boucle.");
                    return;
                }

                if (!string.Equals(
                        context.PrimaryEntityName,
                        EntityQuoteDetail,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("QuoteRueSummaryCalculation: entité non prise en charge.");
                    return;
                }

                var quoteId = GetQuoteId(context, service, tracing);

                if (quoteId == Guid.Empty)
                {
                    tracing.Trace("QuoteRueSummaryCalculation: devis parent introuvable.");
                    return;
                }

                RebuildSummary(service, tracing, quoteId);

                tracing.Trace("QuoteRueSummaryCalculation: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"QuoteRueSummaryCalculation: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans QuoteRueSummaryCalculation : {ex.Message}",
                    ex);
            }
        }

        private static Guid GetQuoteId(
            IPluginExecutionContext context,
            IOrganizationService service,
            ITracingService tracing)
        {
            // Pour Create/Update, Target peut contenir le lookup quoteid.
            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity target)
            {
                var targetQuote =
                    target.GetAttributeValue<EntityReference>(QuoteDetailQuote);

                if (targetQuote != null)
                    return targetQuote.Id;

                // Lors d'un Update, quoteid peut être absent de Target :
                // le lire dans la PreImage.
                if (TryGetQuoteIdFromImage(
                        context.PreEntityImages,
                        PreImageName,
                        out var preImageQuoteId))
                {
                    return preImageQuoteId;
                }

                // Pour Create/Update, récupérer la ligne pour retrouver son devis parent.
                var detailId = target.Id;

                if (detailId == Guid.Empty &&
                    context.OutputParameters.Contains("id") &&
                    context.OutputParameters["id"] is Guid outputId)
                {
                    detailId = outputId;
                }

                if (detailId != Guid.Empty)
                    return RetrieveQuoteId(service, detailId, tracing);
            }

            // Pour Delete, Target est habituellement une EntityReference.
            if (string.Equals(
                    context.MessageName,
                    "Delete",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetQuoteIdFromImage(
                        context.PreEntityImages,
                        PreImageName,
                        out var deleteImageQuoteId))
                {
                    return deleteImageQuoteId;
                }

                if (context.InputParameters.Contains("Target") &&
                    context.InputParameters["Target"] is EntityReference deletedDetail)
                {
                    return RetrieveQuoteId(service, deletedDetail.Id, tracing);
                }
            }

            return Guid.Empty;
        }

        private static bool TryGetQuoteIdFromImage(
            EntityImageCollection images,
            string imageName,
            out Guid quoteId)
        {
            quoteId = Guid.Empty;

            if (images == null || !images.Contains(imageName))
                return false;

            var image = images[imageName];
            var quoteRef = image.GetAttributeValue<EntityReference>(QuoteDetailQuote);

            if (quoteRef == null)
                return false;

            quoteId = quoteRef.Id;
            return quoteId != Guid.Empty;
        }

        private static Guid RetrieveQuoteId(
            IOrganizationService service,
            Guid quoteDetailId,
            ITracingService tracing)
        {
            if (quoteDetailId == Guid.Empty)
                return Guid.Empty;

            var detail = service.Retrieve(
                EntityQuoteDetail,
                quoteDetailId,
                new ColumnSet(QuoteDetailQuote));

            var quoteRef =
                detail.GetAttributeValue<EntityReference>(QuoteDetailQuote);

            if (quoteRef == null)
            {
                tracing.Trace(
                    $"Aucun quoteid trouvé pour la ligne de devis {quoteDetailId}.");

                return Guid.Empty;
            }

            return quoteRef.Id;
        }

        private static void RebuildSummary(
            IOrganizationService service,
            ITracingService tracing,
            Guid quoteId)
        {
            var quoteName = GetQuoteName(service, quoteId, tracing);
            var detailQuery = new QueryExpression(EntityQuoteDetail)
            {
                ColumnSet = new ColumnSet(
                    QuoteDetailGrouping,
                    QuoteDetailBaseAmount,
                    QuoteDetailTax,
                    QuoteDetailExtendedAmount)
            };

            detailQuery.Criteria.AddCondition(
                QuoteDetailQuote,
                ConditionOperator.Equal,
                quoteId);

            var details = service.RetrieveMultiple(detailQuery).Entities;

            tracing.Trace(
                $"QuoteRueSummaryCalculation: {details.Count} lignes de devis lues.");

            var summaries = details
                .Select(detail =>
                {
                    var originalGrouping =
                        detail.GetAttributeValue<string>(QuoteDetailGrouping);

                    var normalizedGrouping = NormalizeGrouping(originalGrouping);

                    return new
                    {
                        Original = originalGrouping,
                        Normalized = normalizedGrouping,
                        Ht = GetMoneyValue(detail, QuoteDetailBaseAmount),
                        Tax = GetMoneyValue(detail, QuoteDetailTax),
                        Ttc = GetMoneyValue(detail, QuoteDetailExtendedAmount)
                    };
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Normalized))
                .GroupBy(row => row.Normalized)
                .Select(group => new SummaryRow
                {
                    Grouping = group.First().Original.Trim(),
                    TotalHt = RoundMoney(group.Sum(row => row.Ht)),
                    TotalTax = RoundMoney(group.Sum(row => row.Tax)),
                    TotalTtc = RoundMoney(group.Sum(row => row.Ttc))
                })
                .OrderBy(row => row.Grouping)
                .ToList();

            DeleteExistingSummaries(service, tracing, quoteId);

            foreach (var row in summaries)
            {
                var summary = new Entity(EntitySummary)
                {
                    [SummaryName] = BuildSummaryName(quoteName, row.Grouping),
                    [SummaryQuote] = new EntityReference(EntityQuote, quoteId),
                    [SummaryGrouping] = row.Grouping,
                    [SummaryHt] = new Money(row.TotalHt),
                    [SummaryTax] = new Money(row.TotalTax),
                    [SummaryTtc] = new Money(row.TotalTtc)
                };

                service.Create(summary);

                tracing.Trace(
                    $"Synthèse créée : {row.Grouping}; HT={row.TotalHt}; " +
                    $"Taxes={row.TotalTax}; TTC={row.TotalTtc}");
            }
        }

        private static void DeleteExistingSummaries(
            IOrganizationService service,
            ITracingService tracing,
            Guid quoteId)
        {
            var query = new QueryExpression(EntitySummary)
            {
                ColumnSet = new ColumnSet(SummaryPrimaryId)
            };

            query.Criteria.AddCondition(
                SummaryQuote,
                ConditionOperator.Equal,
                quoteId);

            var existingSummaries = service.RetrieveMultiple(query).Entities;

            tracing.Trace(
                $"QuoteRueSummaryCalculation: {existingSummaries.Count} anciennes synthèses à supprimer.");

            foreach (var summary in existingSummaries)
                service.Delete(EntitySummary, summary.Id);
        }

        private static string GetQuoteName(
            IOrganizationService service,
            Guid quoteId,
            ITracingService tracing)
        {
            var quote = service.Retrieve(
                EntityQuote,
                quoteId,
                new ColumnSet("name"));

            var name = quote.GetAttributeValue<string>("name");

            if (string.IsNullOrWhiteSpace(name))
            {
                tracing.Trace("Nom du devis vide; utilisation du nom de remplacement.");
                return "Synthèse devis";
            }

            return name;
        }

        private static string NormalizeGrouping(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().ToLowerInvariant();
        }

        private static string BuildSummaryName(string quoteName, string grouping)
        {
            return $"{quoteName} - {grouping}";
        }

        private static decimal GetMoneyValue(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var value) ||
                value == null)
            {
                return 0m;
            }

            return value is Money money ? money.Value : 0m;
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private sealed class SummaryRow
        {
            public string Grouping { get; set; }
            public decimal TotalHt { get; set; }
            public decimal TotalTax { get; set; }
            public decimal TotalTtc { get; set; }
        }
    }
}