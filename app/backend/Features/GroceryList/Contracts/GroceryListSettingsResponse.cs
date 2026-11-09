namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The list's in-app settings, as <c>GET /api/grocery-list</c>, <c>PATCH /api/grocery-list/settings</c> and the
/// realtime hub send them.
/// </summary>
/// <param name="Version">Increases with every change. Clients keep the settings with the highest version.</param>
/// <param name="ImageGenerationEnabled">Whether new and renamed items get an AI-drawn picture.</param>
/// <param name="ImageGenerationAvailable">Whether the server can draw pictures at all: it has a Gemini API key. When
/// it can't, <paramref name="ImageGenerationEnabled"/> is kept but has no effect.</param>
public sealed record GroceryListSettingsResponse(int Version, bool ImageGenerationEnabled, bool ImageGenerationAvailable);
