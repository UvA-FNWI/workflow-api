namespace UvA.Workflow.Notifications;

public record MailButton(string Label, string Url);

public interface IMailLayout
{
    string Render(string htmlBody, MailButton? button = null);
}

public class DefaultMailLayout : IMailLayout
{
    public string Render(string htmlBody, MailButton? button = null)
    {
        var buttonHtml = button is null
            ? ""
            : $"""
               <tr>
                 <td align="center" style="padding: 0 0 24px 0;">
                   <a href="{button.Url}"
                      style="display:inline-block;padding:12px 28px;background-color:#E00031;color:#ffffff;
                             font-family:Source Sans Pro, Arial, sans-serif;font-size:14px;
                             text-decoration:none;border-radius:2px;">
                     {button.Label}
                   </a>
                 </td>
               </tr>
               """;

        return $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Milestones</title>
                </head>
                <body style="margin:0;padding:0;background-color:#F5F5F4;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0"
                         style="background-color:#F5F5F4;padding:32px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="600" cellspacing="0" cellpadding="0"
                               style="background-color:#ffffff;border-radius:6px;overflow:hidden;
                                      box-shadow:0 1px 4px rgba(0,0,0,0.08);">

                          <!-- Body -->
                          <tr>
                            <td align="center" style="padding:24px;font-family:Source Sans Pro, Arial, sans-serif;font-size:14px;
                                       line-height:1.6;color:#333333;">
                              {htmlBody}
                            </td>
                          </tr>

                          <!-- Optional button -->
                          {buttonHtml}

                          <!-- Footer -->
                          <tr>
                            <td style="padding:16px 32px;background-color:#FAFAFA;
                                       border-top:1px solid #F5F5F4;font-family:Source Sans Pro, Arial, sans-serif;
                                       font-size:12px;color:#8F8884;text-align:center;">
                              This is an automated message from UvA Milestones. Please do not reply directly to this email.
                            </td>
                          </tr>

                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;
    }
}