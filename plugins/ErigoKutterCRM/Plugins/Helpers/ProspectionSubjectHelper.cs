using DivicoKutterCRM.Plugins.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Security.Principal;

namespace DivicoKutterCRM.Plugins.Helpers
{
    public static class ProspectionSubjectHelper
    {
        public static string GenerateSubject(
            IOrganizationService service,
            Entity prospection)
        {
            var contactName = GetLookupName(
                service,
                prospection,
                FieldNames.ContactPrincipal,
                "fullname");

            var clientName = GetLookupName(
                service,
                prospection,
                FieldNames.Client,
                "name");

            var scheduledStart = prospection.GetAttributeValue<DateTime?>(
                FieldNames.ScheduledStart) ?? DateTime.UtcNow;

            var dateText = scheduledStart.ToString("yyyyMMdd");

            var ownerInitials = GetOwnerInitials(service, prospection);

            if (string.IsNullOrWhiteSpace(contactName))
            {
                contactName = "Prospection";
            }

            if (string.IsNullOrWhiteSpace(clientName))
            {
                clientName = "Client";
            }

            return $"{contactName} | {clientName} | {dateText} | {ownerInitials}";
        }

        private static string GetLookupName(
            IOrganizationService service,
            Entity prospection,
            string lookupField,
            string nameField)
        {
            var reference = prospection.GetAttributeValue<EntityReference>(
                lookupField);

            if (reference == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(reference.Name))
            {
                return reference.Name;
            }

            try
            {
                var record = service.Retrieve(
                    reference.LogicalName,
                    reference.Id,
                    new ColumnSet(nameField));

                return record.GetAttributeValue<string>(nameField);
            }
            catch
            {
                return null;
            }
        }

        private static string GetOwnerInitials(
            IOrganizationService service,
            Entity prospection)
        {
            var owner = prospection.GetAttributeValue<EntityReference>(
                FieldNames.OwnerId);

            if (owner == null || owner.LogicalName != "systemuser")
            {
                return "XX";
            }

            var user = service.Retrieve(
                "systemuser",
                owner.Id,
                new ColumnSet(FieldNames.FirstName, FieldNames.LastName));

            var firstName = user.GetAttributeValue<string>(FieldNames.FirstName);
            var lastName = user.GetAttributeValue<string>(FieldNames.LastName);

            var firstInitial = string.IsNullOrWhiteSpace(firstName)
                ? "X"
                : firstName.Substring(0, 1);

            var lastInitial = string.IsNullOrWhiteSpace(lastName)
                ? "X"
                : lastName.Substring(0, 1);

            return $"{firstInitial}{lastInitial}".ToUpperInvariant();
        }
    }
}