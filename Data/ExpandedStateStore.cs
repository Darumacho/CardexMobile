namespace CardexMobile.Data;

// Persiste l'état replié/déplié des blocs (Collection, sections par tag, groupes de séries sur
// Sets) via Microsoft.Maui.Storage.Preferences (SharedPreferences côté Android) — plus simple que
// d'étendre AppSettings (partagé/dupliqué avec le desktop) pour un état purement d'affichage
// mobile. Par défaut tout est déplié (true) : seules les sections explicitement repliées par
// l'utilisateur occupent une entrée.
public static class ExpandedStateStore
{
    private const string Prefix = "expanded.";

    public static bool Get(string key) => Preferences.Default.Get(Prefix + key, true);

    public static void Set(string key, bool value) => Preferences.Default.Set(Prefix + key, value);
}
