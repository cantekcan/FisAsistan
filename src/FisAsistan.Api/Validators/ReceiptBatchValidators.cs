using FisAsistan.Application.Receipts.Dtos;
using FluentValidation;

namespace FisAsistan.Api.Validators;

public class UpdateBatchRegionsRequestValidator : AbstractValidator<UpdateBatchRegionsRequest>
{
    public UpdateBatchRegionsRequestValidator()
    {
        RuleFor(x => x.Regions).NotEmpty().WithMessage("En az bir bölge belirtilmelidir.");

        RuleForEach(x => x.Regions).ChildRules(region =>
        {
            region.RuleFor(r => r.Corners).Must(c => c.Length == 4)
                .WithMessage("Her bölge tam olarak 4 köşe noktası içermelidir.");
        });
    }
}
