using System.Text.RegularExpressions;
using Android.Graphics;
using Android.Media;
using Cardex.Models;
using CardexMobile.Data;
using CardexMobile.ViewModels;
using Microsoft.EntityFrameworkCore;
using Plugin.Maui.OCR;

namespace CardexMobile.Services;

// Résultat d'un scan : texte brut (passage 1), texte de la zone recadrée si tenté (passage 2,
// debug uniquement), numéro/nom détectés, et les correspondances trouvées dans le catalogue.
public record CardScanOutcome(
    string RawText,
    string? NumberRegionText,
    string DetectedNumber,
    string DetectedName,
    IReadOnlyList<CardListItem> Matches);

// Pipeline OCR → numéro/nom → correspondances catalogue, extrait de ScanTestPageViewModel pour être
// réutilisé à l'identique par [[LiveScanPageViewModel]] (scan caméra en continu) — voir
// [[project_ocr_scan]] pour l'historique complet des corrections appliquées ici.
public class CardScanService(MobileAppDbContext db, DbInitializer dbInit)
{
    // Numéro de collection : "4/102", "SWSH001/SWSH073", "TG01/TG30"... — préfixe lettres optionnel
    // de chaque côté du "/" (sets promo/spéciaux), pas de suffixe (aucun cas réel n'en a — un
    // suffixe-lettre observé en test venait d'un symbole de rareté collé à l'OCR, ex. "144t").
    // Les totaux réels ne dépassent jamais 3 chiffres, donc on plafonne à 3 des deux côtés ; pas de
    // \b final, pour qu'un chiffre parasite collé juste après (ex. "0883" au lieu de "088") soit
    // simplement ignoré plutôt que de faire échouer tout le match.
    private static readonly Regex NumberPattern = new(
        @"\b([A-Za-z]{0,4}\d{1,3})\s*/\s*([A-Za-z]{0,4}\d{1,3})",
        RegexOptions.Compiled);

    // Préfixe lettres optionnel + chiffres, pour séparer p. ex. "SWSH001" en ("SWSH", 1) ou
    // "096" en ("", 96). Réutilisé pour comparer le numéro/total OCR au catalogue (Number y est
    // stocké non préfixé de zéros, ex. "4" et non "004").
    private static readonly Regex PrefixDigitsPattern = new(@"^([A-Za-z]*)0*(\d*)$", RegexOptions.Compiled);

    // "/" est un simple trait, visuellement proche d'un I/L/l majuscule ou minuscule — confusion OCR
    // classique (observée en test : "143L086" au lieu de "143/086"). On ne corrige que lorsque la
    // lettre est collée entre deux chiffres (contexte de numéro), jamais ailleurs dans le texte.
    private static readonly Regex SlashLookalikePattern = new(@"(?<=\d)\s*[ILl]\s*(?=\d)", RegexOptions.Compiled);

    // Variante composée observée en test : le "/" lu comme I/L/l ET le "0" du total qui suit lu
    // comme la lettre O juste derrière, collés ensemble (ex. "143LO86" au lieu de "143/086"). Prioritaire
    // sur le motif simple ci-dessus, sinon le "O" isolé empêche le lookahead chiffre de matcher.
    private static readonly Regex SlashZeroLookalikePattern = new(@"(?<=\d)\s*[ILl]\s*[Oo](?=\d)", RegexOptions.Compiled);

    private static string NormalizeForNumberMatch(string text) =>
        SlashLookalikePattern.Replace(SlashZeroLookalikePattern.Replace(text, "/0"), "/");

    // "Evolves from X" cite le nom de la carte précédente dans la chaîne d'évolution, pas la carte
    // elle-même — source de faux positifs confirmée en test (ex. "Evolves from Nosepass" sur une
    // carte Probopass faisait remonter Nosepass par erreur). Retiré du texte avant la comparaison de
    // nom seulement (le texte brut affiché à l'écran n'est pas modifié).
    private static readonly Regex EvolvesFromPattern = new(@"evolves\s+from\s+\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<CardScanOutcome> ScanAsync(byte[] photoBytes)
    {
        await dbInit.ReadyTask;

        // InitAsync est idempotent côté plugin (pas de registration DI nécessaire pour ce
        // plugin — OcrPlugin.Default s'auto-instancie).
        await OcrPlugin.Default.InitAsync();

        // CameraX (scan live) ne pivote jamais les pixels de la capture : il pose juste un tag EXIF
        // indiquant comment l'afficher, en laissant l'image dans l'orientation native du capteur
        // (paysage, même pour une carte portrait). Le plugin OCR appelle ML Kit avec une rotation
        // figée à 0 en interne (pas modifiable depuis ici) — on pivote donc nous-mêmes AVANT l'OCR,
        // pour que les coordonnées des éléments (utilisées par le recadrage de secours ci-dessous)
        // restent cohérentes avec une image droite plutôt que le paysage brut du capteur.
        photoBytes = await EnsureUprightAsync(photoBytes);

        // Retour utilisateur : l'OCR peine en faible luminosité (texte sombre, peu contrasté). On ne
        // corrige QUE si la photo est détectée comme sombre (voir EnhanceForLowLightAsync), pour ne
        // pas risquer de dégrader les photos déjà bien éclairées qui fonctionnent déjà. Ne touche
        // pas aux dimensions : les coordonnées OCR (FindNumberRegionBox) restent valables, et le
        // recadrage de secours ci-dessous repart de photoBytes d'origine, pas de cette version.
        var ocrBytes = await EnhanceForLowLightAsync(photoBytes);

        var result = await OcrPlugin.Default.RecognizeTextAsync(ocrBytes, tryHard: true);
        if (!result.Success)
            throw new InvalidOperationException("OCR failed to process the image.");

        var rawText = result.AllText ?? string.Join('\n', result.Lines);

        // Juste les dimensions, sans décoder les pixels (rapide) — sert à localiser la zone du
        // numéro pour le recadrage de secours ci-dessous.
        var boundsOptions = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(photoBytes, 0, photoBytes.Length, boundsOptions);
        var imageWidth = boundsOptions.OutWidth;
        var imageHeight = boundsOptions.OutHeight;

        var numberBox = FindNumberRegionBox(imageWidth, imageHeight, result.Elements);

        var match = NumberPattern.Match(NormalizeForNumberMatch(rawText));

        // Le numéro n'est pas ressorti du passage sur la photo complète (texte dilué, trop
        // petit à l'échelle de toute la carte) : on retente avec un second passage OCR recadré
        // et zoomé juste sur la zone du bas qui contient des chiffres, contraste renforcé pour
        // atténuer les reflets holo. Coûte un peu de temps, donc seulement en secours.
        string? numberRegionText = null;
        if (!match.Success && numberBox is { } retryBox)
        {
            numberRegionText = await TryOcrNumberRegionAsync(photoBytes, retryBox);
            if (numberRegionText is not null)
                match = NumberPattern.Match(NormalizeForNumberMatch(numberRegionText));
        }

        string? detectedNumberPart = null;
        string detectedNumber;
        if (match.Success)
        {
            // "O" confondu avec "0" en début de numéro zéro-préfixé (ex. "O59" pour "069") :
            // corrigé aussi à l'affichage, pas seulement pour la recherche interne.
            var normNumber = NormalizeLeadingO(match.Groups[1].Value);
            var normTotal = NormalizeLeadingO(match.Groups[2].Value);
            detectedNumber = $"{normNumber}/{normTotal}";
            detectedNumberPart = normNumber;
        }
        else
        {
            detectedNumber = "Not found";
        }

        var (detectedName, matches) = await FindMatchesAsync(detectedNumberPart, rawText);

        return new CardScanOutcome(rawText, numberRegionText, detectedNumber, detectedName, matches);
    }

    // Marque une carte comme possédée (+1) directement depuis une ligne de résultat du scan — même
    // logique que SetDetailPageViewModel.SaveOwnedAsync (créer/incrémenter OwnedCards + vérifier les
    // succès liés à la possession), dupliquée ici car ScanTestPageViewModel n'a pas de contexte EF
    // propre et CardScanService est déjà le point d'accès DB du flux scan. Retourne la nouvelle
    // quantité pour que l'appelant mette à jour l'item affiché sans recharger toute la liste.
    public async Task<int> IncrementOwnedAsync(string cardId, string setId)
    {
        var entry = await db.OwnedCards.FindAsync(cardId);
        int newQuantity;
        if (entry is null)
        {
            newQuantity = 1;
            db.OwnedCards.Add(new OwnedCard { CardId = cardId, SetId = setId, Quantity = newQuantity });
        }
        else
        {
            entry.Quantity++;
            newQuantity = entry.Quantity;
        }
        await db.SaveChangesAsync();
        await AchievementChecks.CheckOwnershipAsync(db, newQuantity);
        return newQuantity;
    }

    // Lit le tag EXIF "orientation" et pivote réellement les pixels si besoin — sans dépendance
    // supplémentaire (Android.Media.ExifInterface accepte un flux depuis l'API 24, notre minSdk).
    // Ne touche à rien si l'image est déjà droite (tag normal/absent) : sans risque de régression
    // sur les photos prises via l'appli caméra système, qui semblent déjà correctement orientées.
    private static async Task<byte[]> EnsureUprightAsync(byte[] bytes)
    {
        int degrees;
        using (var exifStream = new MemoryStream(bytes))
        {
            var exif = new ExifInterface(exifStream);
            degrees = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal) switch
            {
                (int)Orientation.Rotate90 => 90,
                (int)Orientation.Rotate180 => 180,
                (int)Orientation.Rotate270 => 270,
                _ => 0
            };
        }
        if (degrees == 0) return bytes;

        using var bitmap = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
        if (bitmap is null) return bytes;

        using var matrix = new Matrix();
        matrix.PostRotate(degrees);
        using var rotated = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);

        using var outStream = new MemoryStream();
        rotated.Compress(Bitmap.CompressFormat.Jpeg!, 95, outStream);
        return outStream.ToArray();
    }

    // Luminance moyenne en-dessous de laquelle une photo est considérée comme sombre (sur 255) —
    // seuil choisi empiriquement, pas de mesure précise disponible pour l'affiner davantage.
    private const int DarkLuminanceThreshold = 100;
    private const int TargetLuminance = 150;

    // Corrige luminosité/contraste UNIQUEMENT si la photo est détectée comme sombre (voir
    // ComputeAverageLuminance) — une photo déjà bien éclairée n'est jamais touchée, pour ne pas
    // risquer de dégrader les scans qui fonctionnent déjà bien. Contrairement au recadrage de
    // secours (TryOcrNumberRegionAsync), ne redimensionne/ne recadre rien : les dimensions restent
    // identiques, donc les coordonnées OCR utilisées plus loin (FindNumberRegionBox) restent valables.
    private static async Task<byte[]> EnhanceForLowLightAsync(byte[] bytes)
    {
        using var bitmap = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
        if (bitmap is null) return bytes;

        var avgLuminance = ComputeAverageLuminance(bitmap);
        if (avgLuminance >= DarkLuminanceThreshold) return bytes;

        var brightnessBoost = Math.Clamp(TargetLuminance - avgLuminance, 0, 120);

        using var enhanced = Bitmap.CreateBitmap(bitmap.Width, bitmap.Height, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(enhanced))
        using (var paint = new Android.Graphics.Paint())
        {
            // Contraste léger (plus doux que le recadrage de secours, qui lui passe en niveaux de
            // gris) + décalage additif proportionnel à l'écart par rapport à la luminance cible.
            const float contrast = 1.15f;
            var translate = (-0.5f * contrast + 0.5f) * 255f + brightnessBoost;
            var matrix = new ColorMatrix(
            [
                contrast, 0, 0, 0, translate,
                0, contrast, 0, 0, translate,
                0, 0, contrast, 0, translate,
                0, 0, 0, 1, 0
            ]);
            paint.SetColorFilter(new ColorMatrixColorFilter(matrix));
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }

        using var outStream = new MemoryStream();
        enhanced.Compress(Bitmap.CompressFormat.Jpeg!, 95, outStream);
        return outStream.ToArray();
    }

    // Échantillonne une version réduite (50x50) plutôt que chaque pixel de la photo complète — la
    // luminance moyenne n'a pas besoin de précision pixel-perfect, juste d'être rapide.
    private static int ComputeAverageLuminance(Bitmap bitmap)
    {
        const int sampleSize = 50;
        using var thumb = Bitmap.CreateScaledBitmap(bitmap, sampleSize, sampleSize, true);
        var pixels = new int[sampleSize * sampleSize];
        thumb.GetPixels(pixels, 0, sampleSize, 0, 0, sampleSize, sampleSize);

        long sum = 0;
        foreach (var pixel in pixels)
        {
            var r = (pixel >> 16) & 0xFF;
            var g = (pixel >> 8) & 0xFF;
            var b = pixel & 0xFF;
            sum += (long)(0.299 * r + 0.587 * g + 0.114 * b);
        }
        return (int)(sum / pixels.Length);
    }

    // Boîte englobante serrée (en pixels, sans marge) des éléments contenant un chiffre dans la
    // bande des 30% inférieurs de l'image — le numéro de collection y est toujours imprimé. Sert de
    // base au recadrage OCR de secours (TryOcrNumberRegionAsync), qui ajoute sa propre marge.
    private static (int X, int Y, int Width, int Height)? FindNumberRegionBox(
        int imageWidth, int imageHeight, IList<OcrResult.OcrElement> elements)
    {
        var bottomThreshold = imageHeight * 0.70;
        var digitElements = elements.Where(e => e.Y >= bottomThreshold && e.Text.Any(char.IsDigit)).ToList();
        if (digitElements.Count == 0) return null;

        var minX = digitElements.Min(e => e.X);
        var minY = digitElements.Min(e => e.Y);
        var maxX = digitElements.Max(e => e.X + e.Width);
        var maxY = digitElements.Max(e => e.Y + e.Height);
        var width = maxX - minX;
        var height = maxY - minY;
        return width > 0 && height > 0 ? (minX, minY, width, height) : null;
    }

    // Recadre la photo originale sur la zone du numéro détectée (voir FindNumberRegionBox), avec une
    // marge généreuse autour (contrairement à l'overlay, ce recadrage a besoin de contexte), l'agrandit
    // et renforce son contraste, puis relance l'OCR juste sur ce petit recadrage. Un texte minuscule
    // noyé dans toute la photo devient proportionnellement bien plus grand et net une fois isolé —
    // but : récupérer le "/" ou les chiffres perdus quand le premier passage sur la photo entière
    // échoue (texte dilué, reflets holo).
    private static async Task<string?> TryOcrNumberRegionAsync(byte[] originalBytes, (int X, int Y, int Width, int Height) tightBox)
    {
        using var original = await BitmapFactory.DecodeByteArrayAsync(originalBytes, 0, originalBytes.Length);
        if (original is null) return null;

        var marginX = tightBox.Width / 3 + 20;
        var marginY = tightBox.Height / 2 + 20;
        var x = Math.Max(0, tightBox.X - marginX);
        var y = Math.Max(0, tightBox.Y - marginY);
        var right = Math.Min(original.Width, tightBox.X + tightBox.Width + marginX);
        var bottom = Math.Min(original.Height, tightBox.Y + tightBox.Height + marginY);
        var width = right - x;
        var height = bottom - y;
        if (width <= 0 || height <= 0) return null;
        using var cropped = Bitmap.CreateBitmap(original, x, y, width, height);

        const int scaleFactor = 4;
        using var scaled = Bitmap.CreateScaledBitmap(cropped, width * scaleFactor, height * scaleFactor, true);

        // Niveaux de gris + contraste renforcé : atténue les reflets holo qui brouillent le texte fin,
        // sans dépendance externe (juste une ColorMatrix Android standard).
        using var enhanced = Bitmap.CreateBitmap(scaled.Width, scaled.Height, Bitmap.Config.Argb8888);
        using (var canvas = new Canvas(enhanced))
        using (var paint = new Android.Graphics.Paint())
        {
            const float contrast = 1.8f;
            const float translate = (-0.5f * contrast + 0.5f) * 255f;
            var grayscaleContrast = new ColorMatrix(
            [
                0.33f * contrast, 0.33f * contrast, 0.33f * contrast, 0, translate,
                0.33f * contrast, 0.33f * contrast, 0.33f * contrast, 0, translate,
                0.33f * contrast, 0.33f * contrast, 0.33f * contrast, 0, translate,
                0, 0, 0, 1, 0
            ]);
            paint.SetColorFilter(new ColorMatrixColorFilter(grayscaleContrast));
            canvas.DrawBitmap(scaled, 0, 0, paint);
        }

        using var outStream = new MemoryStream();
        enhanced.Compress(Bitmap.CompressFormat.Jpeg!, 95, outStream);

        var cropResult = await OcrPlugin.Default.RecognizeTextAsync(outStream.ToArray(), tryHard: true);
        return cropResult.Success ? (cropResult.AllText ?? string.Join('\n', cropResult.Lines)) : null;
    }

    // Nom de base (sans suffixe/préfixe) + numéro déjà découpé en (préfixe, chiffres), précalculés
    // UNE SEULE FOIS par carte — pas à chaque scan. Mesuré en test : le vrai coût d'un scan n'est pas
    // le rechargement du catalogue (~200ms) mais le recalcul de ces deux éléments pour les ~21k
    // cartes à chaque fois (regex de découpage de numéro en tête, souvent 1-2s). Cache statique
    // partagé entre toutes les instances (le service est enregistré en Transient, un cache
    // d'instance serait donc systématiquement vide) — sûr car `CachedCards` ne change qu'à une
    // synchronisation/mise à jour du catalogue, jamais pendant la session.
    private readonly record struct ScoringCard(CachedCard Card, string BaseName, string NumberPrefix, int? NumberDigits);

    private static List<ScoringCard>? _scoringCache;
    private static readonly SemaphoreSlim ScoringCacheLock = new(1, 1);

    // Appelé en tâche de fond au démarrage de l'app (voir MauiProgram.cs), pour que le cache soit déjà
    // prêt avant le premier scan réel de l'utilisateur plutôt que de lui faire payer ce coût. Attend
    // explicitement que la base soit prête (migrations/seed) avant de lire le catalogue — sinon on
    // figerait un cache vide/incomplet pour toute la session. Best-effort : un échec ici ne doit
    // jamais empêcher l'app de démarrer, le cache se reconstruira simplement au premier scan réel.
    public async Task WarmupAsync()
    {
        try
        {
            await dbInit.ReadyTask;
            await GetScoringCacheAsync();
        }
        catch
        {
            // Ignoré volontairement — voir commentaire ci-dessus.
        }
    }

    private async Task<List<ScoringCard>> GetScoringCacheAsync()
    {
        if (_scoringCache is not null) return _scoringCache;
        await ScoringCacheLock.WaitAsync();
        try
        {
            if (_scoringCache is not null) return _scoringCache;
            var allCards = await db.CachedCards.ToListAsync();
            _scoringCache = allCards.Select(c =>
            {
                var baseName = StripRuleBoxSuffix(c.Name);
                var (numberPrefix, numberDigits) = SplitPrefixDigits(c.Number);
                return new ScoringCard(c, baseName, numberPrefix, numberDigits);
            }).ToList();
        }
        finally
        {
            ScoringCacheLock.Release();
        }
        return _scoringCache;
    }

    // Scoring unifié sur tout le catalogue (~21k cartes, acceptable pour une action ponctuelle
    // déclenchée par l'utilisateur) : chaque carte reçoit un score cumulé numéro+nom, sans filtrage
    // strict par palier. Le total du set est VOLONTAIREMENT ignoré comme signal de désambiguïsation
    // — la plupart des sets modernes ont des full art/secrètes qui dépassent le total "nominal"
    // imprimé sur la carte (observé : Probopass imprimé "96/88", total catalogue réel 124), donc
    // comparer le total OCR au total catalogue compare deux notions différentes et induit en erreur
    // plus qu'il n'aide. Objectif assumé : une liste de quelques candidats, dont le bon fait partie,
    // pas une résolution automatique à un seul résultat garanti juste.
    private async Task<(string DetectedName, IReadOnlyList<CardListItem> Matches)> FindMatchesAsync(string? numberPart, string rawText)
    {
        (string? numberPrefix, int? numberDigits) = (null, null);
        if (numberPart is not null)
        {
            var (prefix, digits) = SplitPrefixDigits(numberPart);
            numberPrefix = prefix;
            numberDigits = digits;
        }
        var detectedDigits = numberDigits?.ToString();
        var textForNameMatch = EvolvesFromPattern.Replace(rawText, "");
        var translations = await PokemonNameTranslations.LoadAsync();

        var scoringCards = await GetScoringCacheAsync();

        var allScored = scoringCards
            .Select(x =>
            {
                var nameMatch = x.BaseName.Length >= 3 && ContainsWord(textForNameMatch, x.BaseName);
                if (!nameMatch && translations.TryGetValue(x.BaseName, out var foreignNames))
                    nameMatch = foreignNames.Any(fn => fn.Length >= 2 && ContainsWord(textForNameMatch, fn));
                var closeness = NumberCloseness(x.NumberPrefix, x.NumberDigits, numberPrefix, detectedDigits);
                var score = closeness + (nameMatch ? 2 : 0);
                return (Card: x.Card, NameMatch: nameMatch, Score: score);
            })
            .Where(x => x.Score > 0)
            .ToList();

        if (allScored.Count == 0) return ("Not found", []);

        // Ne garder que le MEILLEUR score, pas un top fixe systématique : quand un candidat corrobore
        // déjà numéro ET nom, c'est lui (et ses éventuels ex-aequo) qu'on veut voir seuls — pas noyé
        // sous des correspondances partielles plus faibles. Plafond généreux (30) uniquement parce
        // qu'à score égal tous les candidats ont le même niveau de confiance — le seul risque est d'en
        // couper un par ordre alphabétique (ex. "Lugia" avant "Lugia V" quand aucun numéro ne départage).
        var maxScore = allScored.Max(x => x.Score);
        var scored = allScored
            .Where(x => x.Score == maxScore)
            .OrderBy(x => x.Card.Name)
            .Take(30)
            .ToList();

        if (scored.Count == 0) return ("Not found", []);

        // Reflète ce qui a réellement contribué au score, pas une extraction indépendante : le nom
        // du premier candidat du meilleur palier dont le nom a été repéré dans le texte OCR (base
        // sans suffixe V/ex/...), ou "Not found" si le palier ne retient que des cartes par proximité
        // de numéro sans aucune confirmation de nom.
        var detectedName = scored.FirstOrDefault(x => x.NameMatch).Card?.Name ?? "Not found";

        var setIds = scored.Select(x => x.Card.SetId).Distinct().ToList();
        var sets = await db.CachedSets
            .Where(s => setIds.Contains(s.SetId))
            .ToDictionaryAsync(s => s.SetId, s => s);

        var cardIds = scored.Select(x => x.Card.CardId).ToList();
        var ownedQty = await db.OwnedCards
            .Where(o => cardIds.Contains(o.CardId))
            .ToDictionaryAsync(o => o.CardId, o => o.Quantity);

        var matches = scored.Select(x => ToCardListItem(x.Card, sets.GetValueOrDefault(x.Card.SetId), ownedQty)).ToList();
        return (detectedName, matches);
    }

    // Proximité numéro, en points : 3 = identique, 2 = un seul chiffre différent à longueur égale
    // (ex. "59" vs "69"), 1 = partage le premier ou le dernier chiffre (rattrape un chiffre perdu/en
    // trop par l'OCR, qui change la longueur — ex. "85195" au lieu de "185195"), 0 = rien de commun.
    // Un préfixe lettres (ex. "TG01") est canonique : comparaison stricte, pas de proximité partielle.
    // Prend (catPrefix, catDigits) déjà séparés (voir ScoringCache) — évite de refaire tourner la
    // regex de découpage sur chaque carte à chaque scan (mesuré comme le vrai goulot, pas le
    // rechargement du catalogue lui-même).
    private static int NumberCloseness(string? catPrefix, int? catDigitsValue, string? prefix, string? detectedDigits)
    {
        if (detectedDigits is null) return 0;

        if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(catPrefix))
            return string.Equals(prefix, catPrefix, StringComparison.OrdinalIgnoreCase)
                   && catDigitsValue?.ToString() == detectedDigits ? 3 : 0;

        var cat = catDigitsValue?.ToString() ?? "";
        if (cat == detectedDigits) return 3;
        if (cat.Length == detectedDigits.Length)
        {
            var diff = 0;
            for (var i = 0; i < cat.Length; i++)
                if (cat[i] != detectedDigits[i]) diff++;
            if (diff == 1) return 2;
        }
        if (cat.Length > 0 && (cat[0] == detectedDigits[0] || cat[^1] == detectedDigits[^1]))
            return 1;
        return 0;
    }

    private static CardListItem ToCardListItem(CachedCard card, CachedSet? set, Dictionary<string, int> ownedQty) =>
        new()
        {
            CardId = card.CardId,
            Name = card.Name,
            Number = card.Number,
            SetId = card.SetId,
            SetName = set?.Name ?? card.SetId,
            ImageUrl = card.ImageSmall,
            Rarity = card.Rarity,
            OwnedQuantity = ownedQty.GetValueOrDefault(card.CardId),
            CmLow = card.CmLow,
            TcgLow = card.TcgLow
        };

    private static (string Prefix, int? Digits) SplitPrefixDigits(string s)
    {
        var m = PrefixDigitsPattern.Match(s);
        if (!m.Success) return (s, null);
        var digitsStr = m.Groups[2].Value;
        return (m.Groups[1].Value, digitsStr.Length > 0 ? int.Parse(digitsStr) : null);
    }

    private static string NormalizeLeadingO(string s) =>
        s.Length > 1 && (s[0] == 'O' || s[0] == 'o') && char.IsDigit(s[1]) ? "0" + s[1..] : s;

    // Contains() seul laisse passer un nom court comme sous-chaîne d'un mot sans rapport (observé en
    // test : le Trainer catalogue "Bea" matchait via "unbeabsichtigt" dans un texte OCR allemand).
    // On exige donc que le caractère immédiatement avant/après ne soit pas une lettre — un mot entier,
    // pas un fragment noyé dans un mot composé plus long (fréquent en allemand).
    private static bool ContainsWord(string haystack, string needle)
    {
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = idx == 0 || !char.IsLetter(haystack[idx - 1]);
            var afterPos = idx + needle.Length;
            // Frontière valide si non-lettre ensuite, OU si un badge de règle spéciale est collé
            // sans séparateur juste derrière (ex. "Sylveonex" pour "Sylveon ex", "Espeonex") —
            // observé deux fois en test, l'OCR ne laisse quasiment jamais d'espace avant ce logo.
            var after = afterPos >= haystack.Length || !char.IsLetter(haystack[afterPos])
                || BareRuleBoxContinuations.Any(suf =>
                    haystack.AsSpan(afterPos).StartsWith(suf, StringComparison.OrdinalIgnoreCase)
                    && (afterPos + suf.Length >= haystack.Length || !char.IsLetter(haystack[afterPos + suf.Length])));
            if (before && after) return true;
            idx++;
        }
        return false;
    }

    // Les badges de règle spéciale (V, VMAX, VSTAR, ex, GX...) sont des logos stylisés que l'OCR ne
    // lit quasiment jamais comme texte (observé sur "Lugia V" et "Electivire ex") — comparer le nom
    // catalogue SANS son suffixe au texte brut plutôt que le nom complet, sous peine de ne jamais
    // matcher par nom la quasi-totalité des cartes modernes.
    // Espace ("Lugia V") pour les sets récents, tiret ("Victini-EX") pour la convention ère XY/Sun &
    // Moon côté pokemontcg.io — 335 cartes du catalogue utilisent ce second format (confirmé en
    // test : "Victini-EX" #24/113 ne matchait aucun résultat tant que seul " EX" était reconnu).
    // "★" (Pokémon Gold Star, ex. "Regice ★") et "δ" (Delta Species, ex. "Metagross δ") sont des
    // symboles Unicode littéraux dans le nom catalogue — jamais lus tels quels par l'OCR (confondus
    // avec une lettre, ex. "★"→"u"), même logique que les suffixes de règle habituels.
    private static readonly string[] RuleBoxSuffixes =
        [" VMAX", " VSTAR", " ex", " EX", " GX", " V", "-EX", "-GX", " ★", " δ"];

    // Les mêmes suffixes, sans l'espace/tiret de séparation — utilisé par ContainsWord pour
    // reconnaître un badge collé directement au nom (voir plus haut).
    private static readonly string[] BareRuleBoxContinuations =
        RuleBoxSuffixes.Select(s => s.TrimStart(' ', '-')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Préfixe possessif d'un nom de dresseur (ex. "Cynthia's Garchomp ex", "Iono's Bellibolt ex") —
    // toute une famille de cartes modernes où ce préfixe est stylisé différemment du nom du Pokémon
    // et bien plus fragile à l'OCR (observé en test : "Cynthia's" lu "Gmti", "Garchomp" lu
    // proprement). Comparer juste l'espèce ("Garchomp") plutôt que le nom complet avec préfixe.
    private static readonly Regex PossessivePrefixPattern = new(@"^\S+['’]s\s+", RegexOptions.Compiled);

    private static string StripRuleBoxSuffix(string name)
    {
        // En boucle : certaines cartes cumulent deux badges (ex. "Altaria ex δ") — s'arrêter au
        // premier trouvé laisserait l'autre collé et casserait la comparaison de nom.
        bool stripped;
        do
        {
            stripped = false;
            foreach (var suffix in RuleBoxSuffixes)
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    stripped = true;
                    break;
                }
        } while (stripped);

        return PossessivePrefixPattern.Replace(name, "");
    }
}
