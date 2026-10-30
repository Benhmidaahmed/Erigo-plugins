using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace BSIT.ProspectionConversion
{
    public sealed class ConvertProspection : IPlugin
    {
        private const string ApiName = "bsc_ConvertProspection";

        private const string SourceTable = "bsc_prospection";
        private const string TargetTable = "opportunity";

        private const string SourceSubject = "subject";
        private const string SourceClient = "bsc_client";
        private const string SourceContact = "bsc_contactprincipal";
        private const string SourceBudget = "bsc_montantestime";
        private const string SourceRegion = "bsc_region";

        private const string TargetName = "name";
        private const string TargetClient = "customerid";
        private const string TargetContact = "bsc_contact";
        private const string TargetBudget = "budgetamount";
        private const string TargetSource = "bsc_prospection";
        private const string TargetRegion = "bsc_region";
        private const string TargetCommercial = "bsc_commercial";
        private const string TargetSecteur = "bsc_secteur_ccq";

        private const string SecteurTable = "bsc_secteurs_ccq";
        private const string SecteurPrimaryId = "bsc_secteurs_ccqid";

        private const string CurrencyColumn = "transactioncurrencyid";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            var tracing =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            var factory =
                (IOrganizationServiceFactory)serviceProvider.GetService(
                    typeof(IOrganizationServiceFactory));

            var service =
                factory.CreateOrganizationService(context.UserId);

            try
            {
                if (!string.Equals(
                    context.MessageName,
                    ApiName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        "Ce plugin doit être appelé par la Custom API prévue.");
                }

                string mode = Input(context, "Mode");

                if (mode != "Preview" && mode != "Convert")
                {
                    throw new InvalidPluginExecutionException(
                        "Mode invalide : utiliser Preview ou Convert.");
                }

                Guid sourceId;

                if (!Guid.TryParse(
                        Input(context, "ProspectionId"),
                        out sourceId) ||
                    sourceId == Guid.Empty)
                {
                    throw new InvalidPluginExecutionException(
                        "L'identifiant de la prospection est invalide.");
                }

                EntityMetadata sourceMetadata =
                    GetMetadata(service, SourceTable);

                EntityMetadata targetMetadata =
                    GetMetadata(service, TargetTable);

                RequireColumn(sourceMetadata, SourceSubject);
                RequireColumn(sourceMetadata, SourceClient);
                RequireColumn(sourceMetadata, SourceContact);
                RequireColumn(sourceMetadata, SourceBudget);

                RequireLookupColumn(
                    sourceMetadata,
                    SourceRegion);

                RequireColumn(targetMetadata, TargetName);
                RequireColumn(targetMetadata, TargetBudget);

                RequireLookupTarget(
                    targetMetadata,
                    TargetSource,
                    SourceTable);

                bool uniqueKeyActive =
                    targetMetadata.Keys != null &&
                    targetMetadata.Keys.Any(k =>
                        k.KeyAttributes != null &&
                        k.KeyAttributes.Length == 1 &&
                        k.KeyAttributes[0] == TargetSource &&
                        k.EntityKeyIndexStatus ==
                            EntityKeyIndexStatus.Active);

                if (!uniqueKeyActive)
                {
                    throw new InvalidPluginExecutionException(
                        "Configuration incomplète : active une clé alternative " +
                        "sur opportunity.bsc_prospection avant la conversion.");
                }

                string[] columns =
                {
                    SourceSubject,
                    SourceClient,
                    SourceContact,
                    SourceBudget,
                    SourceRegion,
                    CurrencyColumn
                };

                string[] existingColumns = columns
                    .Where(c => FindColumn(sourceMetadata, c) != null)
                    .ToArray();

                Entity source = service.Retrieve(
                    SourceTable,
                    sourceId,
                    new ColumnSet(existingColumns));

                EntityReference client =
                    source.GetAttributeValue<EntityReference>(
                        SourceClient);

                EntityReference contact =
                    source.GetAttributeValue<EntityReference>(
                        SourceContact);

                EntityReference region =
                    source.GetAttributeValue<EntityReference>(
                        SourceRegion);

                EntityReference currency =
                    source.GetAttributeValue<EntityReference>(
                        CurrencyColumn);

                decimal? sourceBudget =
                    ReadNumber(source, SourceBudget);

                Guid? existingId = FindOpportunity(
                    service,
                    targetMetadata.PrimaryIdAttribute,
                    sourceId);

                context.OutputParameters["OpportunityId"] =
                    existingId.HasValue
                        ? existingId.Value.ToString("D")
                        : "";

                context.OutputParameters["AlreadyConverted"] =
                    existingId.HasValue;

                context.OutputParameters["DefaultName"] =
                    source.GetAttributeValue<string>(
                        SourceSubject) ?? "";

                context.OutputParameters["ClientName"] =
                    client == null
                        ? ""
                        : (client.Name ?? client.LogicalName);

                context.OutputParameters["BudgetText"] =
                    sourceBudget.HasValue
                        ? sourceBudget.Value.ToString(
                            CultureInfo.InvariantCulture)
                        : "";

                context.OutputParameters["CurrencyName"] =
                    ReadCurrencyName(service, currency);

                // Aucun changement de données en mode Preview.
                if (mode == "Preview")
                {
                    return;
                }

                // Une conversion existante n'est pas modifiée.
                if (existingId.HasValue)
                {
                    return;
                }

                string opportunityName =
                    Input(context, "OpportunityName").Trim();

                if (string.IsNullOrWhiteSpace(opportunityName))
                {
                    throw new InvalidPluginExecutionException(
                        "Le nom de l'opportunité est obligatoire.");
                }

                ValidateText(
                    targetMetadata,
                    TargetName,
                    opportunityName,
                    "Le nom de l'opportunité");

                string commercialText =
                    Input(context, "CommercialText").Trim();

                ValidateText(
                    targetMetadata,
                    TargetCommercial,
                    commercialText,
                    "Le nom du commercial");

                string budgetText =
                    Input(context, "BudgetText").Trim();

                decimal? budget = null;

                if (budgetText.Length > 0)
                {
                    decimal parsedBudget;

                    if (!decimal.TryParse(
                        budgetText,
                        NumberStyles.AllowLeadingSign |
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out parsedBudget))
                    {
                        throw new InvalidPluginExecutionException(
                            "Le montant budgétaire est invalide.");
                    }

                    if (parsedBudget < 0)
                    {
                        throw new InvalidPluginExecutionException(
                            "Le montant budgétaire ne peut pas être négatif.");
                    }

                    budget = parsedBudget;
                }

                if (client == null)
                {
                    throw new InvalidPluginExecutionException(
                        "Renseigne le client sur la prospection avant conversion.");
                }

                RequireLookupTarget(
                    targetMetadata,
                    TargetClient,
                    client.LogicalName);

                if (contact != null)
                {
                    RequireLookupTarget(
                        targetMetadata,
                        TargetContact,
                        contact.LogicalName);
                }

                RequireLookupColumn(
                    targetMetadata,
                    TargetRegion);

                if (region != null)
                {
                    RequireLookupTarget(
                        targetMetadata,
                        TargetRegion,
                        region.LogicalName);
                }

                RequireLookupTarget(
                    targetMetadata,
                    TargetSecteur,
                    SecteurTable);

                if (!(FindColumn(targetMetadata, TargetBudget)
                    is MoneyAttributeMetadata))
                {
                    throw new InvalidPluginExecutionException(
                        "La colonne opportunity.budgetamount " +
                        "doit être de type devise.");
                }

                if (budget.HasValue && currency == null)
                {
                    throw new InvalidPluginExecutionException(
                        "Un montant a été saisi, mais la prospection " +
                        "n'a pas de devise. Renseigne sa devise " +
                        "avant conversion.");
                }

                if (currency != null)
                {
                    RequireLookupTarget(
                        targetMetadata,
                        CurrencyColumn,
                        currency.LogicalName);
                }

                string secteurIdText =
                    Input(context, "SecteurId").Trim();

                EntityReference secteur = null;

                if (!string.IsNullOrWhiteSpace(secteurIdText))
                {
                    Guid secteurId;

                    if (!Guid.TryParse(
                            secteurIdText,
                            out secteurId) ||
                        secteurId == Guid.Empty)
                    {
                        throw new InvalidPluginExecutionException(
                            "L'identifiant du secteur CCQ est invalide.");
                    }

                    // Vérifie l'existence du secteur et son accès en lecture.
                    service.Retrieve(
                        SecteurTable,
                        secteurId,
                        new ColumnSet(SecteurPrimaryId));

                    secteur = new EntityReference(
                        SecteurTable,
                        secteurId);
                }

                var opportunity = new Entity(TargetTable);

                opportunity[TargetName] = opportunityName;

                opportunity[TargetSource] =
                    new EntityReference(
                        SourceTable,
                        sourceId);

                opportunity[TargetClient] = client;

                if (contact != null)
                {
                    opportunity[TargetContact] = contact;
                }

                if (region != null)
                {
                    opportunity[TargetRegion] =
                        new EntityReference(
                            region.LogicalName,
                            region.Id);
                }

                if (secteur != null)
                {
                    opportunity[TargetSecteur] = secteur;
                }

                if (!string.IsNullOrWhiteSpace(commercialText))
                {
                    opportunity[TargetCommercial] =
                        commercialText;
                }

                if (currency != null)
                {
                    opportunity[CurrencyColumn] = currency;
                }

                if (budget.HasValue)
                {
                    opportunity[TargetBudget] =
                        new Money(budget.Value);
                }

                // Ne pas affecter bsc_code :
                // l'auto-numérotation Dataverse le génère.
                Guid opportunityId =
                    service.Create(opportunity);

                context.OutputParameters["OpportunityId"] =
                    opportunityId.ToString("D");

                context.OutputParameters["AlreadyConverted"] =
                    false;

                tracing.Trace(
                    "Conversion réussie. Prospection={0}, Opportunité={1}",
                    sourceId,
                    opportunityId);
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracing.Trace(
                    "Validation de conversion. CorrelationId={0}. Message={1}",
                    context.CorrelationId,
                    ex.Message);

                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "Erreur de conversion. CorrelationId={0}. Exception={1}",
                    context.CorrelationId,
                    ex);

                // Aucune tentative de récupération par écriture
                // après une erreur transactionnelle.
                throw new InvalidPluginExecutionException(
                    "Conversion impossible. Vérifie les droits, " +
                    "les champs obligatoires, les références et " +
                    "les opportunités liées avant toute nouvelle tentative. " +
                    "Détail : " + ex.Message);
            }
        }

        private static string Input(
            IPluginExecutionContext context,
            string name)
        {
            if (!context.InputParameters.Contains(name) ||
                context.InputParameters[name] == null)
            {
                return "";
            }

            return Convert.ToString(
                context.InputParameters[name],
                CultureInfo.InvariantCulture);
        }

        private static EntityMetadata GetMetadata(
            IOrganizationService service,
            string table)
        {
            var response =
                (RetrieveEntityResponse)service.Execute(
                    new RetrieveEntityRequest
                    {
                        LogicalName = table,
                        EntityFilters = EntityFilters.All,
                        RetrieveAsIfPublished = false
                    });

            return response.EntityMetadata;
        }

        private static AttributeMetadata FindColumn(
            EntityMetadata metadata,
            string column)
        {
            return metadata.Attributes.FirstOrDefault(
                a => a.LogicalName == column);
        }

        private static void RequireColumn(
            EntityMetadata metadata,
            string column)
        {
            if (FindColumn(metadata, column) == null)
            {
                throw new InvalidPluginExecutionException(
                    "Colonne introuvable : " +
                    metadata.LogicalName + "." + column);
            }
        }

        private static LookupAttributeMetadata RequireLookupColumn(
            EntityMetadata metadata,
            string column)
        {
            var lookup =
                FindColumn(metadata, column)
                as LookupAttributeMetadata;

            if (lookup == null)
            {
                throw new InvalidPluginExecutionException(
                    "La colonne " +
                    metadata.LogicalName + "." + column +
                    " doit être de type recherche.");
            }

            return lookup;
        }

        private static void RequireLookupTarget(
            EntityMetadata metadata,
            string column,
            string expectedTable)
        {
            LookupAttributeMetadata lookup =
                RequireLookupColumn(metadata, column);

            if (lookup.Targets == null ||
                !lookup.Targets.Contains(expectedTable))
            {
                throw new InvalidPluginExecutionException(
                    "Lookup incompatible : " +
                    metadata.LogicalName + "." + column +
                    " doit accepter la table " +
                    expectedTable + ".");
            }
        }

        private static void ValidateText(
            EntityMetadata metadata,
            string column,
            string value,
            string fieldLabel)
        {
            var textMetadata =
                FindColumn(metadata, column)
                as StringAttributeMetadata;

            if (textMetadata == null)
            {
                throw new InvalidPluginExecutionException(
                    "La colonne " +
                    metadata.LogicalName + "." + column +
                    " doit être de type texte.");
            }

            if (textMetadata.MaxLength.HasValue &&
                value.Length > textMetadata.MaxLength.Value)
            {
                throw new InvalidPluginExecutionException(
                    fieldLabel +
                    " dépasse la longueur autorisée : " +
                    textMetadata.MaxLength.Value +
                    " caractères.");
            }
        }

        private static decimal? ReadNumber(
            Entity entity,
            string column)
        {
            if (!entity.Attributes.Contains(column) ||
                entity[column] == null)
            {
                return null;
            }

            object value = entity[column];

            if (value is Money)
            {
                return ((Money)value).Value;
            }

            if (value is decimal)
            {
                return (decimal)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is double)
            {
                return Convert.ToDecimal(value);
            }

            throw new InvalidPluginExecutionException(
                "Le montant source doit être une devise ou un nombre.");
        }

        private static Guid? FindOpportunity(
            IOrganizationService service,
            string primaryIdColumn,
            Guid sourceId)
        {
            var query = new QueryExpression(TargetTable)
            {
                ColumnSet =
                    new ColumnSet(primaryIdColumn),
                TopCount = 1
            };

            query.Criteria.AddCondition(
                TargetSource,
                ConditionOperator.Equal,
                sourceId);

            Entity row =
                service.RetrieveMultiple(query)
                    .Entities.FirstOrDefault();

            return row == null
                ? (Guid?)null
                : row.Id;
        }

        private static string ReadCurrencyName(
            IOrganizationService service,
            EntityReference currency)
        {
            if (currency == null)
            {
                return "";
            }

            Entity row = service.Retrieve(
                currency.LogicalName,
                currency.Id,
                new ColumnSet("currencyname"));

            return row.GetAttributeValue<string>(
                "currencyname") ?? "";
        }
    }
}