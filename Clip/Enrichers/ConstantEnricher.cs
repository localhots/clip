namespace Clip.Enrichers;

internal sealed class ConstantEnricher(Field field) : ILogEnricher
{
    public void Enrich(List<Field> target) => target.Add(field);
}
