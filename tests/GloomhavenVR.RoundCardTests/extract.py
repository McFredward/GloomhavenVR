import pathlib, sys
root = pathlib.Path(sys.argv[1])
source = (root / 'src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs').read_text()
def method(signature):
    start = source.index(signature)
    brace = source.index('{', start)
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]
methods = [method(s) for s in ['private void CollectRoundCards(', 'private static bool HasLeftTheRound(', 'private static void OrderRoundPairByInitiative(']]
# This harness executes the complete production collector, not a reimplemented acceptance rule.
pathlib.Path(sys.argv[2]).write_text('using ScenarioRuleLibrary;\ninternal sealed partial class CardsDriver {\n' + '\n'.join(methods) + '\n}')
print('Round-card source bindings: complete collector, departure predicate, initiative ordering')
