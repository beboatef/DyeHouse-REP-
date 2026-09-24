using FluentValidation;
using MediatR;
using ApplicationValidationException = DyeHouseERP.Application.Common.Exceptions.ValidationException;

namespace DyeHouseERP.Application.Common.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for a request before the
/// handler executes. Every Command/Query in the Application layer gets
/// validation for free just by having a matching Validator registered.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure != null)
            .ToList();

        if (failures.Count != 0)
            throw new ApplicationValidationException(failures);

        return await next();
    }
}
