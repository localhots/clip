using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Clip.Fields;

internal static class FieldExtractor
{
    private static volatile Hashtable _cache = new();
    private static readonly Lock WriteLock = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExtractInto(object source, List<Field> target)
    {
        // Hashtable is documented as thread-safe for concurrent reads without synchronization.
        // This avoids ConcurrentDictionary's striped lock metadata overhead on the hot path.
        var extractor = (Action<object, List<Field>>?)_cache[source.GetType()];
        if (extractor != null)
        {
            extractor(source, target);
            return;
        }

        ExtractSlow(source, target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExtractSlow(object source, List<Field> target)
    {
        var type = source.GetType();

        // Guard against accidental misuse
        if (type.IsPrimitive || source is string || source is Array)
            throw new ArgumentException(
                $"Expected an anonymous type or dictionary for fields, got {type.Name}. " +
                "Use new { Key = value } syntax.", nameof(source));

        switch (source)
        {
            // Generic dictionary fast paths
            case IDictionary<string, object?> dict:
                foreach (var kvp in dict)
                    target.Add(new Field(kvp.Key, kvp.Value));
                return;
            case IReadOnlyDictionary<string, object?> roDict:
                foreach (var kvp in roDict)
                    target.Add(new Field(kvp.Key, kvp.Value));
                return;
            // Non-generic IDictionary
            case IDictionary nonGenericDict:
                foreach (DictionaryEntry entry in nonGenericDict)
                    target.Add(new Field(entry.Key.ToString()!, entry.Value));
                return;
            default:
                // POCO / anonymous type path — compiled expression tree
                var compiled = CompileExtractor(type);

                // Copy-on-write: create a new Hashtable with existing + new entry
                lock (WriteLock)
                {
                    var updated = new Hashtable(_cache)
                    {
                        [type] = compiled,
                    };
                    _cache = updated; // Atomic reference swap
                }

                compiled(source, target);
                break;
        }
    }

    private static Action<object, List<Field>> CompileExtractor(Type type)
    {
        var src = Expression.Parameter(typeof(object), "src");
        var list = Expression.Parameter(typeof(List<Field>), "list");
        var typed = Expression.Variable(type, "typed");

        var body = new List<Expression> { Expression.Assign(typed, Expression.Convert(src, type)) };
        var addMethod = typeof(List<Field>).GetMethod("Add")!;

        var stringCtor = typeof(Field).GetConstructor([typeof(string), typeof(string)])!;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var value = Expression.Property(typed, prop);

            // Enums: convert to string name via ToString()
            if (prop.PropertyType.IsEnum)
            {
                var toStr = prop.PropertyType.GetMethod("ToString", Type.EmptyTypes)!;
                body.Add(Expression.Call(list, addMethod,
                    Expression.New(stringCtor, Expression.Constant(prop.Name),
                        Expression.Call(value, toStr))));
                continue;
            }

            var ctor = GetFieldCtor(prop.PropertyType);

            Expression[] args = ctor.GetParameters()[1].ParameterType == typeof(object)
                ? [Expression.Constant(prop.Name), Expression.Convert(value, typeof(object))]
                : [Expression.Constant(prop.Name), value];

            body.Add(Expression.Call(list, addMethod, Expression.New(ctor, args)));
        }

        return Expression.Lambda<Action<object, List<Field>>>(
            Expression.Block([typed], body), src, list
        ).Compile();
    }

    /// <summary>
    /// Returns the most specific Field constructor for <paramref name="propType"/>.
    /// Prefers typed constructors (e.g., Field(string, int)) to avoid boxing;
    /// falls back to Field(string, object) for types without a dedicated overload.
    /// Enums are handled separately (converted to string). Nullable types always
    /// use the object fallback since they can't be passed directly to typed constructors.
    /// </summary>
    private static ConstructorInfo GetFieldCtor(Type propType)
    {
        if (Nullable.GetUnderlyingType(propType) == null)
        {
            var ctor = typeof(Field).GetConstructor([typeof(string), propType]);
            if (ctor != null) return ctor;
        }

        return typeof(Field).GetConstructor([typeof(string), typeof(object)])!;
    }
}
