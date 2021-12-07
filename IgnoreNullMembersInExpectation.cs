using System;
using FluentAssertions.Equivalency;

namespace Helper
{
    public class IgnoreNullMembersInExpectation : IEquivalencyStep
    {
        public bool CanHandle(IEquivalencyValidationContext context, IEquivalencyAssertionOptions config) => context.Expectation is null;

        public bool Handle(IEquivalencyValidationContext context, IEquivalencyValidator parent, IEquivalencyAssertionOptions config) => true;
    }
}
