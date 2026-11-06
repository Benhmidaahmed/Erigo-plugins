using DivicoKutterCRM.Plugins.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Security.Principal;

namespace DivicoKutterCRM.Plugins.Prospection
{
    public static class SubjectGenerator
    {
        public static string Generate(
            IOrganizationService service,
            Entity prospection)
        {
            var contact = prospection.GetAttributeValue<EntityReference>(
                FieldNames.ContactPrincipal);

            var municipalite = prospection.GetAttributeValue<EntityReference>(
                FieldNames.Municipalite);

            var contactName = string.IsNullOrWhiteSpace(contact?.Name)
                ? "Prospection"
                : contact.Name;

            var municipaliteName = string.IsNullOrWhiteSpace(municipalite?.Name)
                ? "Municipalité"
                : municipalite.Name;

            var scheduledStart = prospection.GetAttributeValue<DateTime?>(
                FieldNames.ScheduledStart) ?? DateTime.UtcNow;

            var dateText = scheduledStart.ToString("yyyyMMddHHmm");

            var ownerInitials = GetOwnerInitials(service, prospection);

            return
                $"{contactName} | {municipaliteName} | {dateText} | {ownerInitials}";
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