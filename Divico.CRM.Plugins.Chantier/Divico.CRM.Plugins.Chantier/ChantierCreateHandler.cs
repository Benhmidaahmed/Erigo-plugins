using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DivicoKutter.Plugins
{
    public class ChantierCreateHandler : IPlugin
    {
        // Tables
        private const string EntityChantier = "bsc_chantier";
        private const string EntityDevis = "quote";
        private const string EntityContact = "contact";

        // Champs du chantier
        private const string ChantierDevis = "bsc_devisid";
        private const string ChantierClient = "bsc_client";
        private const string ChantierMunicipalite = "bsc_municipalite";
        private const string ChantierContact = "bsc_contactid";
        private const string ChantierOpportunite = "bsc_offreid";
        private const string ChantierNom = "bsc_name";

        // Champs du devis
        private const string DevisMunicipalite = "bsc_municipalite";
        private const string DevisContact = "bsc_parentcontactid";
        private const string DevisClient = "bsc_client";
        private const string DevisOpportunite = "opportunityid";
        private const string DevisNom = "name";

        // À confirmer dans Dataverse : le document fourni n'a pas confirmé ce champ.
        private const string DevisNumeroOffre = "bsc_numoffre";

        // Champ principal de la table Municipalité, selon l'ancien code fourni.
        // Vérifiez ce nom logique dans Dataverse si le nom affiché ne se résout pas.
        private const string MunicipaliteNom = "bsc_name";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(
                typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            var tracing = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));

            if (context.Depth > 1)
            {
                tracing.Trace("ChantierCreateHandler: profondeur > 1, arrêt anti-boucle.");
                return;
            }

            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.Equals(context.PrimaryEntityName, EntityChantier, StringComparison.OrdinalIgnoreCase))
                return;

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity chantier))
                return;

            try
            {
                tracing.Trace("ChantierCreateHandler: début.");

                // Le devis doit être associé au chantier.
                var devisRef = chantier.GetAttributeValue<EntityReference>(ChantierDevis);
                if (devisRef == null)
                {
                    tracing.Trace("ChantierCreateHandler: aucun devis lié; aucune copie effectuée.");
                    return;
                }

                // Lire du devis uniquement les champs nécessaires.
                var devis = service.Retrieve(
                    EntityDevis,
                    devisRef.Id,
                    new ColumnSet(
                        DevisMunicipalite,
                        DevisContact,
                        DevisClient,
                        DevisOpportunite,
                        DevisNom,
                        DevisNumeroOffre
                    )
                );

                // Copier le client du devis vers le chantier.
                var clientRef = devis.GetAttributeValue<EntityReference>(DevisClient);
                if (clientRef != null &&
                    chantier.GetAttributeValue<EntityReference>(ChantierClient) == null)
                {
                    chantier[ChantierClient] = clientRef;
                }

                // Copier la municipalité du devis vers le chantier.
                var municipaliteRef =
                    devis.GetAttributeValue<EntityReference>(DevisMunicipalite);

                if (municipaliteRef != null &&
                    chantier.GetAttributeValue<EntityReference>(ChantierMunicipalite) == null)
                {
                    chantier[ChantierMunicipalite] = municipaliteRef;
                }

                // Copier le contact du devis vers le chantier.
                var contactRef = devis.GetAttributeValue<EntityReference>(DevisContact);
                if (contactRef != null &&
                    chantier.GetAttributeValue<EntityReference>(ChantierContact) == null)
                {
                    chantier[ChantierContact] = contactRef;
                }

                // Copier l'opportunité du devis vers le chantier.
                var opportuniteRef =
                    devis.GetAttributeValue<EntityReference>(DevisOpportunite);

                if (opportuniteRef != null &&
                    chantier.GetAttributeValue<EntityReference>(ChantierOpportunite) == null)
                {
                    chantier[ChantierOpportunite] = opportuniteRef;
                }

                // Préparer le nom du chantier.
                var municipaliteName = GetLookupName(
                    service,
                    municipaliteRef,
                    MunicipaliteNom,
                    tracing);

                var contactName = GetLookupName(
                    service,
                    contactRef,
                    "fullname",
                    tracing);

                var devisName = devis.GetAttributeValue<string>(DevisNom) ?? string.Empty;
                var numeroOffre = devis.GetAttributeValue<string>(DevisNumeroOffre) ?? string.Empty;

                var chantierName = BuildChantierName(
                    municipaliteName,
                    contactName,
                    devisName,
                    numeroOffre);

                // Ne pas écraser un nom déjà fourni.
                if (string.IsNullOrWhiteSpace(
                        chantier.GetAttributeValue<string>(ChantierNom)))
                {
                    chantier[ChantierNom] = chantierName;
                }

                tracing.Trace($"ChantierCreateHandler: nom calculé = {chantierName}");
                tracing.Trace("ChantierCreateHandler: fin.");
            }
            catch (Exception ex)
            {
                tracing.Trace($"ChantierCreateHandler: erreur : {ex}");

                throw new InvalidPluginExecutionException(
                    $"Erreur dans ChantierCreateHandler : {ex.Message}",
                    ex);
            }
        }

        private static string GetLookupName(
            IOrganizationService service,
            EntityReference reference,
            string primaryNameField,
            ITracingService tracing)
        {
            if (reference == null)
                return string.Empty;

            // Le nom peut déjà être fourni par le contexte.
            if (!string.IsNullOrWhiteSpace(reference.Name))
                return reference.Name;

            try
            {
                var related = service.Retrieve(
                    reference.LogicalName,
                    reference.Id,
                    new ColumnSet(primaryNameField));

                return related.GetAttributeValue<string>(primaryNameField)
                       ?? string.Empty;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    $"Impossible de lire le nom de {reference.LogicalName} " +
                    $"avec le champ {primaryNameField} : {ex.Message}");

                return string.Empty;
            }
        }

        private static string BuildChantierName(
            string municipalite,
            string contact,
            string devisName,
            string numeroOffre)
        {
            var parts = new[]
            {
                municipalite,
                contact,
                devisName,
                numeroOffre
            };

            var nonEmptyParts = Array.FindAll(
                parts,
                part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" - ", nonEmptyParts);
        }
    }
}