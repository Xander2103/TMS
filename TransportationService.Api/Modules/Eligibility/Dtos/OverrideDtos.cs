namespace TransportationService.Api.Modules.Eligibility.Dtos;

public record EligibilityOverrideDto(Guid Id, Guid EmployeeId, string RelatedEntityType, Guid? RelatedEntityId, string RuleCode, string Reason, Guid ApprovedByUserId, DateTime ApprovedAt, DateTime? ValidUntil);
public record CreateEligibilityOverrideRequest(Guid EmployeeId, string RelatedEntityType, Guid? RelatedEntityId, string RuleCode, string Reason, DateTime? ValidUntil);
