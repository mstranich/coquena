using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Coquena.Recipes;

// Emit the source tree with its quoting/interpolation semantics intact.
internal static class RecipeIntention
{
    internal static string Yaml(RecipeValue root)
    {
        var text = new StringBuilder();
        var emitter = new Emitter(new StringWriter(text, System.Globalization.CultureInfo.InvariantCulture));
        emitter.Emit(new StreamStart()); emitter.Emit(new DocumentStart());
        Emit(root);
        emitter.Emit(new DocumentEnd(true)); emitter.Emit(new StreamEnd());
        return text.ToString();
        void Emit(RecipeValue value)
        {
            if (value.Kind == RecipeKind.Map)
            {
                emitter.Emit(new MappingStart());
                foreach (var pair in value.Fields)
                {
                    emitter.Emit(new Scalar(null, null, pair.Key, ScalarStyle.DoubleQuoted, true, true)); Emit(pair.Value);
                }
                emitter.Emit(new MappingEnd());
            }
            else if (value.Kind == RecipeKind.Sequence)
            {
                emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Block));
                foreach (var item in value.Items) Emit(item);
                emitter.Emit(new SequenceEnd());
            }
            else
            {
                var style = value.Kind != RecipeKind.String ? ScalarStyle.Plain : value.Style switch
                {
                    RecipeStyle.SingleQuoted => ScalarStyle.SingleQuoted,
                    RecipeStyle.Block => ScalarStyle.Literal,
                    RecipeStyle.Quoted => ScalarStyle.DoubleQuoted,
                    _ => ScalarStyle.Plain
                };
                emitter.Emit(new Scalar(null, null, value.Kind == RecipeKind.Null ? "null" : value.Text ?? "", style, true, true));
            }
        }
    }
}
