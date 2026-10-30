using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    /// <summary>
    /// Copie les lignes du devis vers les produits du chantier lors de la création
    /// d'un chantier.
    ///
    /// Enregistrement recommandé :
    /// - Table : bsc_chantier
    /// - Message : Create
    /// - Stage : PostOperation
    /// - Mode : Synchronous
    /// </summary>
    public class CopierProduitsChantierPlugin : IPlugin
    {
        private const string EntityChantier = "bsc_chantier";
        private const string EntityQuote = "quote";
        private const string EntityQuoteDetail = "quotedetail";
        private const string EntityProduitChantier = "bsc_produit_chantier";

        // Champ du devis sur le chantier
        private const string ChantierDevis = "bsc_devisid";

        // Champs des lignes de devis
        private const string QuoteDetailQuote = "quoteid";
        private const string QuoteDetailProduct = "productid";
        private const string QuoteDetailDescription = "productdescription";
        private const string QuoteDetailGrouping = "bsc_concernant";
        private const string QuoteDetailQuantity = "quantity";
        private const string QuoteDetailUnitPrice = "priceperunit";
        private const string QuoteDetailTaxRate = "bsc_taxepercentage";

        // Champs ERIGO de la table Produit chantier
        private const string ProduitChantierLookup = "bsc_chantier";
        private const string ProduitChantierProduct = "bsc_product";
        private const string ProduitChantierDescription = "bsc_description";
        private const string ProduitChantierGrouping = "bsc_concernant";
        private const string ProduitChantierInitialQuantity = "bsc_qte_initiale";
        private const string ProduitChantierQuantityCompleted = "bsc_qte_realisee_total";
        private const string ProduitChantierUnitPrice = "bsc_prix_unitaire";
        private const string ProduitChantierTaxRate = "bsc_taxepercentage";
        private const string ProduitChantierAmountCompleted = "bsc_montant_realise";
        private const string ProduitChantierName = "bsc_name";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            try
            {
                tracing.Trace(
                    $"CopierProduitsChantierPlugin: début. " +
                    $"Entity={context.PrimaryEntityName}, Message={context.MessageName}, " +
                    $"Depth={context.Depth}");

                if (context.Depth > 1)
                {
                    tracing.Trace("CopierProduitsChantierPlugin: arrêt anti-boucle.");
                    return;
                }

                if (!string.Equals(
                        context.MessageName,
                        "Create",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        context.PrimaryEntityName,
                        EntityChantier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("CopierProduitsChantierPlugin: événement non concerné.");
                    return;
                }

                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity))
                {
                    tracing.Trace("CopierProduitsChantierPlugin: Target absent ou invalide.");
                    return;
                }

                var chantierId = GetCreatedChantierId(context);

                if (chantierId == Guid.Empty)
                {
                    tracing.Trace("CopierProduitsChantierPlugin: ID du chantier introuvable.");
                    return;
                }

                var chantier = service.Retrieve(
                    EntityChantier,
                    chantierId,
                    new ColumnSet(ChantierDevis));

                var devisRef =
                    chantier.GetAttributeValue<EntityReference>(ChantierDevis);

                if (devisRef == null)
                {
                    tracing.Trace(
                        "CopierProduitsChantierPlugin: aucun devis lié au chantier.");

                    return;
                }

                if (!string.Equals(
                        devisRef.LogicalName,
                        EntityQuote,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace(
                        $"CopierProduitsChantierPlugin: le lookup devis pointe vers " +
                        $"{devisRef.LogicalName}, et non quote.");

                    return;
                }

                var query = new QueryExpression(EntityQuoteDetail)
                {
                    ColumnSet = new ColumnSet(
                        QuoteDetailProduct,
                        QuoteDetailDescription,
                        QuoteDetailGrouping,
                        QuoteDetailQuantity,
                        QuoteDetailUnitPrice,
                        QuoteDetailTaxRate)
                };

                query.Criteria.AddCondition(
                    QuoteDetailQuote,
                    ConditionOperator.Equal,
                    devisRef.Id);

                var quoteDetails = service.RetrieveMultiple(query).Entities;

                tracing.Trace(
                    $"CopierProduitsChantierPlugin: {quoteDetails.Count} lignes de devis trouvées.");

                foreach (var quoteDetail in quoteDetails)
                {
                    var produitChantier = new Entity(EntityProduitChantier);

                    produitChantier[ProduitChantierLookup] =
                        new EntityReference(EntityChantier, chantierId);

                    CopyIfPresent(
                        quoteDetail,
                        QuoteDetailProduct,
                        produitChantier,
                        ProduitChantierProduct);

                    CopyIfPresent(
                        quoteDetail,
                        QuoteDetailDescription,
                        produitChantier,
                        ProduitChantierDescription);

                    CopyIfPresent(
                        quoteDetail,
                        QuoteDetailGrouping,
                        produitChantier,
                        ProduitChantierGrouping);

                    CopyIfPresent(
                        quoteDetail,
                        QuoteDetailUnitPrice,
                        produitChantier,
                        ProduitChantierUnitPrice);

                    CopyIfPresent(
                        quoteDetail,
                        QuoteDetailTaxRate,
                        produitChantier,
                        ProduitChantierTaxRate);

                    var quantity =
                        quoteDetail.GetAttributeValue<decimal?>(QuoteDetailQuantity) ?? 0m;

                    produitChantier[ProduitChantierInitialQuantity] = quantity;
                    produitChantier[ProduitChantierQuantityCompleted] = 0m;
                    produitChantier[ProduitChantierAmountCompleted] = new Money(0m);

                    var description =
                        quoteDetail.GetAttributeValue<string>(QuoteDetailDescription);

                    produitChantier[ProduitChantierName] =
                        string.IsNullOrWhiteSpace(description)
                            ? "Produit chantier"
                            : description;

                    var produitChantierId = service.Create(produitChantier);

                    tracing.Trace(
                        $"Produit chantier créé. QuoteDetail={quoteDetail.Id}, " +
                        $"ProduitChantier={produitChantierId}");
                }

                tracing.Trace("CopierProduitsChantierPlugin: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"CopierProduitsChantierPlugin: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans CopierProduitsChantierPlugin : {ex.Message}",
                    ex);
            }
        }

        private static Guid GetCreatedChantierId(IPluginExecutionContext context)
        {
            if (context.OutputParameters.Contains("id") &&
                context.OutputParameters["id"] is Guid outputId)
            {
                return outputId;
            }

            if (context.PrimaryEntityId != Guid.Empty)
                return context.PrimaryEntityId;

            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity target)
            {
                return target.Id;
            }

            return Guid.Empty;
        }

        private static void CopyIfPresent(
            Entity source,
            string sourceAttribute,
            Entity destination,
            string destinationAttribute)
        {
            if (source.Attributes.TryGetValue(sourceAttribute, out var value) &&
                value != null)
            {
                destination[destinationAttribute] = value;
            }
        }
    }
}