using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Erigo.CopyQuoteLineTax.Plugins
{
    public sealed class PopulateQuoteFromOpportunityAndContactPlugin : IPlugin
    {
        private const string QuoteTable = "quote";
        private const string OpportunityTable = "opportunity";
        private const string ContactTable = "contact";

        private const string OpportunityLookup = "opportunityid";

        private const string SourceClient = "parentaccountid";
        private const string SourceContact = "bsc_contact";
        private const string SourceRegion = "bsc_region";
        private const string SourceSector = "bsc_secteur_ccq";

        private const string DestinationClient = "bsc_client";
        private const string DestinationContact = "bsc_parentcontactid";
        private const string DestinationRegion = "bsc_region";
        private const string DestinationSector = "bsc_secteur_ccq";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracing =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            IPluginExecutionContext context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            if (!string.Equals(
                    context.MessageName,
                    "Create",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    context.PrimaryEntityName,
                    QuoteTable,
                    StringComparison.OrdinalIgnoreCase)
                || context.Stage != 20)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            Entity target = context.InputParameters["Target"] as Entity;

            if (target == null || target.LogicalName != QuoteTable)
            {
                return;
            }

            try
            {
                EntityReference opportunityReference =
                    target.GetAttributeValue<EntityReference>(
                        OpportunityLookup);

                if (opportunityReference == null)
                {
                    tracing.Trace(
                        "PopulateQuote : opportunityid absent. Aucun traitement.");
                    return;
                }

                IOrganizationServiceFactory factory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(
                        typeof(IOrganizationServiceFactory));

                IOrganizationService service =
                    factory.CreateOrganizationService(context.UserId);

                Entity opportunity = service.Retrieve(
                    OpportunityTable,
                    opportunityReference.Id,
                    new ColumnSet(
                        SourceClient,
                        SourceContact,
                        SourceRegion,
                        SourceSector));

                tracing.Trace(
                    "PopulateQuote : opportunité lue, Id={0}.",
                    opportunityReference.Id);

                CopyLookupIfEmpty(
                    opportunity,
                    SourceClient,
                    target,
                    DestinationClient,
                    tracing);

                CopyLookupIfEmpty(
                    opportunity,
                    SourceContact,
                    target,
                    DestinationContact,
                    tracing);

                CopyLookupIfEmpty(
                    opportunity,
                    SourceRegion,
                    target,
                    DestinationRegion,
                    tracing);

                CopyLookupIfEmpty(
                    opportunity,
                    SourceSector,
                    target,
                    DestinationSector,
                    tracing);

                // Utilise le contact conservé ou copié sur le devis.
                EntityReference contactReference =
                    target.GetAttributeValue<EntityReference>(
                        DestinationContact);

                if (contactReference == null)
                {
                    tracing.Trace(
                        "PopulateQuote : aucun contact principal. " +
                        "Informations commerciales traitées, adresse non copiée.");
                    return;
                }

                if (!string.Equals(
                        contactReference.LogicalName,
                        ContactTable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        "Le contact principal du devis doit pointer " +
                        "vers la table Contact.");
                }

                Entity contact = service.Retrieve(
                    ContactTable,
                    contactReference.Id,
                    new ColumnSet(
                        "address1_line1",
                        "address1_line2",
                        "address1_city",
                        "address1_postalcode",
                        "address1_country"));

                CopyTextIfEmpty(
                    contact,
                    "address1_line1",
                    target,
                    "bsc_adresse1rue1",
                    tracing);

                CopyTextIfEmpty(
                    contact,
                    "address1_line2",
                    target,
                    "bsc_adresse1rue2",
                    tracing);

                CopyTextIfEmpty(
                    contact,
                    "address1_city",
                    target,
                    "bsc_adresse1ville",
                    tracing);

                CopyTextIfEmpty(
                    contact,
                    "address1_postalcode",
                    target,
                    "bsc_adresse1codepostal",
                    tracing);

                CopyTextIfEmpty(
                    contact,
                    "address1_country",
                    target,
                    "bsc_adresse1pays",
                    tracing);

                tracing.Trace(
                    "PopulateQuote : traitement terminé. Contact={0}.",
                    contactReference.Id);
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracing.Trace(
                    "PopulateQuote : erreur métier : {0}",
                    ex.ToString());

                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "PopulateQuote : erreur technique : {0}",
                    ex.ToString());

                throw new InvalidPluginExecutionException(
                    "Impossible de compléter le devis à partir " +
                    "de l'opportunité et du contact. " +
                    "Consultez les traces du plugin.",
                    ex);
            }
        }

        private static void CopyLookupIfEmpty(
            Entity source,
            string sourceAttribute,
            Entity destination,
            string destinationAttribute,
            ITracingService tracing)
        {
            if (!IsEmpty(destination, destinationAttribute))
            {
                tracing.Trace(
                    "PopulateQuote : {0} déjà renseigné, valeur conservée.",
                    destinationAttribute);
                return;
            }

            EntityReference reference =
                source.GetAttributeValue<EntityReference>(sourceAttribute);

            if (reference == null)
            {
                tracing.Trace(
                    "PopulateQuote : source {0} vide.",
                    sourceAttribute);
                return;
            }

            destination[destinationAttribute] =
                new EntityReference(reference.LogicalName, reference.Id);

            tracing.Trace(
                "PopulateQuote : {0} copié vers {1}.",
                sourceAttribute,
                destinationAttribute);
        }

        private static void CopyTextIfEmpty(
            Entity source,
            string sourceAttribute,
            Entity destination,
            string destinationAttribute,
            ITracingService tracing)
        {
            if (!IsEmpty(destination, destinationAttribute))
            {
                tracing.Trace(
                    "PopulateQuote : {0} déjà renseigné, valeur conservée.",
                    destinationAttribute);
                return;
            }

            string value =
                source.GetAttributeValue<string>(sourceAttribute);

            if (string.IsNullOrWhiteSpace(value))
            {
                tracing.Trace(
                    "PopulateQuote : source {0} vide.",
                    sourceAttribute);
                return;
            }

            destination[destinationAttribute] = value;

            tracing.Trace(
                "PopulateQuote : {0} copié vers {1}.",
                sourceAttribute,
                destinationAttribute);
        }

        private static bool IsEmpty(
            Entity entity,
            string attributeName)
        {
            if (!entity.Attributes.Contains(attributeName)
                || entity[attributeName] == null)
            {
                return true;
            }

            string text = entity[attributeName] as string;

            return text != null && string.IsNullOrWhiteSpace(text);
        }
    }
}