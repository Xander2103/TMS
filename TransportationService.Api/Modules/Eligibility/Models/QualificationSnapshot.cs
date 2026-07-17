namespace TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Qualifications.Entities;

public record QualificationSnapshot(string QualificationTypeCode, QualificationStatus EffectiveStatus, DateOnly? ExpiryDate);
