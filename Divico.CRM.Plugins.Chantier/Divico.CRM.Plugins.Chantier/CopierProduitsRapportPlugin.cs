using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    /// <summary>
    /// Copie les produits du chantier vers les lignes de produits du rapport
    /// lorsqu'un rapport de chantier est créé.
    ///
    /// Enregistrement recommandé :
    /// - Table : bsc_rapport_chantier
    /// - Message : Create
    /// - Stage : PostOperation
    /// - Mode : Synchronous
    /// </summary>
    public class CopierProduitsRapportPlugin : IPlugin
    {
        private const string EntityRapportChantier = "bsc_rapport_chantier";
        private const string EntityChantier = "bsc_chantier";
        private const string EntityProduitChantier = "bsc_produit_chantier";
        private const string EntityProduitRapport = "bsc_produit_rapport";

        // Champs du rapport et des produits chantier
        private const string RapportChantierLookup = "bsc_chantier";
        private const string ProduitChantierLookup = "bsc_chantier";

        // Champs à lire sur Produit chantier
        private const string ProduitChantierProduct = "bsc_product";
        private const string ProduitChantierDescription = "bsc_description";
        private const string ProduitChantierGrouping = "bsc_concernant";
        private const string ProduitChantierUnitPrice = "bsc_prix_unitaire";
        private const string ProduitChantierTaxRate = "bsc_taxepercentage";

        // Champs à créer sur Produit rapport
        private const string ProduitRapportRapportLookup = "bsc_rapport_chantier";
        private const string ProduitRapportProduitChantierLookup = "bsc_produit_chantier";
        private const string ProduitRapportProduct = "bsc_product";
        private const string ProduitRapportDescription = "bsc_description";
        private const string ProduitRapportGrouping = "bsc_concernant";
        private const string ProduitRapportUnitPrice = "bsc_prix_unitaire";
        private const string ProduitRapportTaxRate = "bsc_taxepercentage";
        private const string ProduitRapportQuantity = "bsc_qte_realisee";
        private const string ProduitRapportAmount = "bsc_montant_realise";
        private const string ProduitRapportName = "bsc_name";

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
                    $"CopierProduitsRapportPlugin: début. " +
                    $"Entité={context.PrimaryEntityName}, Message={context.MessageName}, " +
                    $"Depth={context.Depth}");

                if (context.Depth > 1)
                {
                    tracing.Trace("CopierProduitsRapportPlugin: arrêt anti-boucle.");
                    return;
                }

                if (!string.Equals(
                        context.MessageName,
                        "Create",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        context.PrimaryEntityName,
                        EntityRapportChantier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace("CopierProduitsRapportPlugin: événement non concerné.");
                    return;
                }

                var rapportId = GetCreatedRecordId(context);

                if (rapportId == Guid.Empty)
                {
                    tracing.Trace("CopierProduitsRapportPlugin: ID du rapport introuvable.");
                    return;
                }

                var rapport = service.Retrieve(
                    EntityRapportChantier,
                    rapportId,
                    new ColumnSet(RapportChantierLookup));

                var chantierRef =
                    rapport.GetAttributeValue<EntityReference>(RapportChantierLookup);

                if (chantierRef == null)
                {
                    tracing.Trace(
                        "CopierProduitsRapportPlugin: aucun chantier lié au rapport.");

                    return;
                }

                if (!string.Equals(
                        chantierRef.LogicalName,
                        EntityChantier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tracing.Trace(
                        $"CopierProduitsRapportPlugin: le lookup pointe vers " +
                        $"{chantierRef.LogicalName}, et non {EntityChantier}.");

                    return;
                }

                var query = new QueryExpression(EntityProduitChantier)
                {
                    ColumnSet = new ColumnSet(
                        "bsc_produit_chantierid",
                        ProduitChantierProduct,
                        ProduitChantierDescription,
                        ProduitChantierGrouping,
                        ProduitChantierUnitPrice,
                        ProduitChantierTaxRate)
                };

                query.Criteria.AddCondition(
                    ProduitChantierLookup,
                    ConditionOperator.Equal,
                    chantierRef.Id);

                var produitsChantier = service.RetrieveMultiple(query).Entities;

                tracing.Trace(
                    $"CopierProduitsRapportPlugin: {produitsChantier.Count} produits chantier trouvés.");

                foreach (var produitChantier in produitsChantier)
                {
                    var produitRapport = new Entity(EntityProduitRapport)
                    {
                        [ProduitRapportRapportLookup] =
                            new EntityReference(EntityRapportChantier, rapportId),

                        [ProduitRapportProduitChantierLookup] =
                            new EntityReference(
                                EntityProduitChantier,
                                produitChantier.Id),

                        [ProduitRapportQuantity] = 0m,
                        [ProduitRapportAmount] = new Money(0m)
                    };

                    CopyIfPresent(
                        produitChantier,
                        ProduitChantierProduct,
                        produitRapport,
                        ProduitRapportProduct);

                    CopyIfPresent(
                        produitChantier,
                        ProduitChantierDescription,
                        produitRapport,
                        ProduitRapportDescription);

                    CopyIfPresent(
                        produitChantier,
                        ProduitChantierGrouping,
                        produitRapport,
                        ProduitRapportGrouping);

                    CopyIfPresent(
                        produitChantier,
                        ProduitChantierUnitPrice,
                        produitRapport,
                        ProduitRapportUnitPrice);

                    CopyIfPresent(
                        produitChantier,
                        ProduitChantierTaxRate,
                        produitRapport,
                        ProduitRapportTaxRate);

                    var description =
                        produitChantier.GetAttributeValue<string>(
                            ProduitChantierDescription);

                    produitRapport[ProduitRapportName] =
                        string.IsNullOrWhiteSpace(description)
                            ? "Produit rapport"
                            : description;

                    var produitRapportId = service.Create(produitRapport);

                    tracing.Trace(
                        $"Produit rapport créé : {produitRapportId}, " +
                        $"depuis produit chantier : {produitChantier.Id}");
                }

                tracing.Trace("CopierProduitsRapportPlugin: fin avec succès.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"CopierProduitsRapportPlugin: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans CopierProduitsRapportPlugin : {ex.Message}",
                    ex);
            }
        }

        private static Guid GetCreatedRecordId(IPluginExecutionContext context)
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