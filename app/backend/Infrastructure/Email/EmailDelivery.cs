namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// What Cloudflare did with an accepted email, counted in recipients.
/// </summary>
/// <param name="Delivered">Recipients whose mail server took the email straight away.</param>
/// <param name="Queued">Recipients Cloudflare will keep trying for a while (their server was busy, say).</param>
/// <param name="Undeliverable">
/// Recipients it gave up on: the address bounced for good, or is on the account's suppression list.
/// </param>
internal sealed record EmailDelivery(int Delivered, int Queued, int Undeliverable);
