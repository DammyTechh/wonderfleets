"""Generates the WonderFleet transactional email templates from one shared vocabulary.

Email HTML is not web HTML: no external stylesheets, no web fonts, no flexbox, and Outlook
renders through Word. So everything here is tables with inline styles, a system font stack,
and layout that degrades predictably. Icons are drawn as a coloured badge with a glyph rather
than images, because most clients block remote images by default and an alert whose meaning
depends on a blocked image is an alert that failed.

Writing them from one spec keeps all eleven consistent; hand-editing eleven files does not.
"""
from pathlib import Path

OUT = Path('/home/claude/wfsrc/backend/src/WonderFleet.Infrastructure/Notifications/Templates')

INK, MUTED, FAINT = '#18203C', '#687C88', '#98A4AC'
LINE, HAIR = '#DEE4EA', '#EFF3F6'
BRAND, BRAND_DARK = '#63883C', '#455F26'
TONES = {
    'critical': ('#C4403C', '#FBEEEE'),
    'warning':  ('#C08A2C', '#FBF5E8'),
    'normal':   ('#4E8C3C', '#EFF7EC'),
    'info':     ('#3C6AD0', '#EDF2FC'),
    'brand':    (BRAND,     '#F2F6EE'),
}
FONT = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif"


def badge(tone, glyph, eyebrow, heading):
    """The visual anchor: a coloured disc with a glyph, an eyebrow, and the headline."""
    fg, bg = TONES[tone]
    return f"""<table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 16px;">
  <tr>
    <td style="width:40px;padding:0;vertical-align:top;">
      <table role="presentation" cellpadding="0" cellspacing="0" width="40"><tr>
        <td align="center" height="40" style="width:40px;height:40px;background:{bg};border-radius:20px;font-family:{FONT};font-size:19px;line-height:40px;font-weight:700;color:{fg};">{glyph}</td>
      </tr></table>
    </td>
    <td style="padding:0 0 0 14px;vertical-align:top;">
      <div style="font-family:{FONT};font-size:11px;line-height:16px;letter-spacing:1px;text-transform:uppercase;font-weight:700;color:{fg};">{eyebrow}</div>
      <div style="font-family:{FONT};font-size:19px;line-height:27px;font-weight:700;color:{INK};padding-top:2px;">{heading}</div>
    </td>
  </tr>
</table>"""


def lead(text):
    return (f'<p style="margin:0 0 20px;font-family:{FONT};font-size:15px;line-height:24px;'
            f'color:{INK};">{text}</p>')


def rows(pairs):
    """A label/value table. Each row disappears when its value is absent, so one template
    serves callers that supply different amounts of detail."""
    out = [f'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" '
           f'style="margin:0 0 22px;border-top:1px solid {LINE};">']
    for label, key in pairs:
        out.append(f"""  {{{{#{key}}}}}<tr>
    <td style="padding:10px 12px 10px 0;border-bottom:1px solid {HAIR};font-family:{FONT};font-size:13px;line-height:19px;color:{MUTED};width:40%;vertical-align:top;">{label}</td>
    <td style="padding:10px 0;border-bottom:1px solid {HAIR};font-family:{FONT};font-size:14px;line-height:20px;font-weight:600;color:{INK};vertical-align:top;">{{{{{key}}}}}</td>
  </tr>{{{{/{key}}}}}""")
    out.append('</table>')
    return '\n'.join(out)


def button(url_key, label, colour=BRAND):
    """Centre-left call to action. Outlook squares the corners; everything else rounds them."""
    return f"""{{{{#{url_key}}}}}<table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 22px;">
  <tr>
    <td align="center" bgcolor="{colour}" style="border-radius:10px;">
      <a href="{{{{{url_key}}}}}" style="display:inline-block;padding:14px 28px;font-family:{FONT};font-size:15px;line-height:20px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px;">{label}</a>
    </td>
  </tr>
</table>{{{{/{url_key}}}}}"""


def note(text, tone=None):
    if tone:
        fg, bg = TONES[tone]
        return (f'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0;">'
                f'<tr><td style="background:{bg};border-left:3px solid {fg};padding:12px 14px;'
                f'font-family:{FONT};font-size:13px;line-height:20px;color:{INK};">{text}</td></tr></table>')
    return f'<p style="margin:0;font-family:{FONT};font-size:13px;line-height:20px;color:{MUTED};">{text}</p>'


def build(name, subject, parts):
    body = '\n'.join(p for p in parts if p)
    OUT.joinpath(name).write_text(f'<!-- subject: {subject} -->\n{body}\n', encoding='utf-8')
    return name


# ---------------------------------------------------------------- the templates
# Subject tails are conditional: callers do not all supply a route or a trip code, and
# "Your WonderFleet tracking link —" with nothing after the dash looks broken in an inbox.

build('share-link-logistics.html',
      'Your WonderFleet tracking link{{#Route}} — {{Route}}{{/Route}}',
      [badge('info', '&rarr;', 'Live tracking', 'Track your shipment without signing in'),
       lead('Hello {{Name}}, {{OrganisationName}} shared live fleet tracking with you. '
            'The button below opens your dashboard directly &mdash; no account or password is needed.'),
       # FleetList already reads "WF-1237 (origin → destination)", so a separate route row
       # would repeat it. Driver is not supplied by the share-link path, so it is left out.
       rows([('Shipments', 'TripCount'), ('Vehicles', 'FleetList'),
             ('Expected arrival', 'ExpectedArrival'), ('Link expires', 'ExpiresAt')]),
       button('ShareUrl', 'Open fleet tracking'),
       note('Keep this link private &mdash; anyone who has it can see the shipment. '
            'It stops working when the trip ends{{#ExpiresAt}}, and no later than {{ExpiresAt}}{{/ExpiresAt}}.')])

build('share-link-agro-processor.html',
      'Track your produce in transit{{#Route}} — {{Route}}{{/Route}}',
      [badge('brand', '&rarr;', 'Live tracking', 'Follow your produce to its destination'),
       lead('Hello {{Name}}, {{OrganisationName}} shared live tracking of your produce. '
            'You will see its position and the cargo temperature and humidity in real time &mdash; '
            'no account or password is needed.'),
       rows([('Shipments', 'TripCount'), ('Produce', 'Produce'), ('Vehicles', 'FleetList'),
             ('Destination', 'DestinationLabel'), ('Expected arrival', 'ExpectedArrival'),
             ('Link expires', 'ExpiresAt')]),
       button('ShareUrl', 'Open live tracking'),
       note('Keep this link private &mdash; anyone who has it can see the shipment. '
            'It stops working when the trip ends{{#ExpiresAt}}, and no later than {{ExpiresAt}}{{/ExpiresAt}}.')])

build('critical-alert.html',
      '[Critical] {{AlertTitle}}{{#TripCode}} — {{TripCode}}{{/TripCode}}',
      [badge('critical', '!', 'Critical alert', '{{AlertTitle}}'),
       lead('Hello {{Name}}, WonderFleet detected a condition that puts the cargo at risk. '
            'The reading is outside the limits set for this shipment.'),
       rows([('Reading', 'Reading'), ('Allowed range', 'AllowedRange'), ('Detected', 'TriggeredAt'),
             ('Shipment', 'TripCode'), ('Fleet', 'FleetNumber'), ('Route', 'Route'),
             ('Driver', 'DriverName'), ('Device', 'DeviceSerial')]),
       button('ActionUrl', 'Open live tracking', TONES['critical'][0]),
       note('{{Message}}', 'critical')])

build('alert-resolved.html',
      'Resolved: {{AlertTitle}}{{#TripCode}} — {{TripCode}}{{/TripCode}}',
      [badge('normal', '&#10003;', 'Resolved', '{{AlertTitle}} is back within limits'),
       lead('Hello {{Name}}, the condition has cleared and the cargo is back inside its '
            'safe range. No further action is needed.'),
       rows([('Resolved at', 'ResolvedAt'), ('Open for', 'Duration'), ('Shipment', 'TripCode'),
             ('Fleet', 'FleetNumber'), ('Route', 'Route')]),
       button('ActionUrl', 'View the shipment'),
       note('{{ResolutionNote}}')])

build('trip-dispatched.html',
      'Shipment {{TripCode}} dispatched{{#Route}} — {{Route}}{{/Route}}',
      [badge('brand', '&rarr;', 'On the road', 'Shipment {{TripCode}} has been dispatched'),
       lead('Hello {{Name}}, the load has left the pickup point. Live tracking and cargo '
            'monitoring are active for the whole journey.'),
       rows([('Produce', 'Produce'), ('Weight', 'Weight'), ('Route', 'Route'),
             ('Fleet', 'FleetNumber'), ('Driver', 'DriverName'), ('Departed', 'StartedAt'),
             ('Expected arrival', 'ExpectedArrival'), ('Temperature limits', 'TemperatureRange'),
             ('Humidity limits', 'HumidityRange'), ('Carrier', 'PartnerName'),
             ('Processor', 'ProcessorName')]),
       button('ActionUrl', 'Follow the shipment'),
       note('You will be alerted the moment conditions move outside the limits above.')])

build('trip-delivered.html',
      'Shipment {{TripCode}} delivered{{#Route}} — {{Route}}{{/Route}}',
      [badge('normal', '&#10003;', 'Delivered', 'Shipment {{TripCode}} has arrived'),
       lead('Hello {{Name}}, the load has been delivered. Here is how the journey went.'),
       rows([('Delivered at', 'DeliveredAt'), ('On time', 'OnTime'), ('Produce', 'Produce'),
             ('Route', 'Route'), ('Fleet', 'FleetNumber'), ('Distance', 'Distance'),
             ('CO&#8322;', 'Co2'), ('Carrier', 'PartnerName'), ('Processor', 'ProcessorName')]),
       button('ActionUrl', 'View the delivery record'),
       note('The tracking link for this shipment has now expired.')])

build('delivery-delayed.html',
      'Delay on shipment {{TripCode}}{{#Route}} — {{Route}}{{/Route}}',
      [badge('warning', '!', 'Running late', 'Shipment {{TripCode}} is behind schedule'),
       lead('Hello {{Name}}, this shipment is not going to arrive when it was expected. '
            'The cargo is still being monitored.'),
       rows([('Delay', 'DelayDuration'), ('Expected arrival', 'ExpectedArrival'),
             ('Last position', 'LastPosition'), ('Route', 'Route'),
             ('Fleet', 'FleetNumber'), ('Driver', 'DriverName')]),
       button('ActionUrl', 'Open live tracking', TONES['warning'][0]),
       note('Longer time on the road means longer heat exposure. Check the cargo readings '
            'before the load is accepted.', 'warning')])

build('partner-onboarded.html',
      'Welcome to WonderFleet, {{OrganisationName}}',
      [badge('brand', '&#10003;', 'Welcome', '{{OrganisationName}} is on WonderFleet'),
       lead('Hello {{Name}}, your organisation has been added to WonderFleet by '
            'OfeminiAgricTech. You will receive a tracking link for each shipment you are '
            'part of &mdash; there is no account to create.'),
       rows([('Organisation', 'OrganisationName'), ('Partner code', 'PartnerCode'),
             ('Partner type', 'PartnerType')]),
       note('Your account is pending activation. Once it is active, shipments can be '
            'scheduled and you will start receiving tracking links.')])

build('partner-status-changed.html',
      'WonderFleet account status: {{Status}}{{#OrganisationName}} — {{OrganisationName}}{{/OrganisationName}}',
      [badge('warning', '!', 'Status change', '{{OrganisationName}} is now {{Status}}'),
       lead('Hello {{Name}}, the WonderFleet status of your organisation has changed.'),
       rows([('Organisation', 'OrganisationName'), ('New status', 'Status'), ('Reason', 'Reason')]),
       note('While suspended, existing tracking links are revoked and no new shipments can '
            'be scheduled. Contact OfeminiAgricTech if you believe this is in error.')])

build('password-changed.html',
      'Your WonderFleet password was changed',
      [badge('info', '&#10003;', 'Security', 'Your password was changed'),
       lead('Hello {{Name}}, the password on your WonderFleet administrator account was '
            'changed. Every other signed-in session was signed out.'),
       rows([('Account', 'Email'), ('Changed at', 'ChangedAt'), ('From address', 'IpAddress')]),
       note('If this was not you, reset your password immediately and tell OfeminiAgricTech.',
            'critical')])

build('test-email.html',
      'WonderFleet email delivery test',
      [badge('normal', '&#10003;', 'Delivery test', 'Email delivery is working'),
       lead('Hello {{Name}}, this is a test sent from WonderFleet Settings. It travelled the '
            'same path as a cargo alert, so alerts will reach you here too.'),
       rows([('Sent from', 'SentFrom'), ('Requested at', 'RequestedAt')]),
       note('No action is needed. If this landed in spam, add the sending address to your '
            'contacts so cargo alerts are not missed.')])

print(f'{len(list(OUT.glob("*.html"))) - 1} templates generated')
