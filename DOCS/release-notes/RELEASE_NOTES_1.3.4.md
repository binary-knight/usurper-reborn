# Usurper Reborn v1.3.4

## Changelist

### Accounts

• Adjusted new passwords to need at least 8 characters; existing shorter passwords still log in
• Improved login with a forgot password choice after a failed login, on a server that sends mail
• Improved registration with an optional email address for password resets, confirmed by a link
• Improved the first login after 1.3.4 with a one-time optional question for an email address
• Improved the online character screen with an Email menu (E) to see, change or remove the address
• Improved the SSH and desktop logins to show the website reset address after a failed login, on a server that sends mail
• Improved the email question at registration and first login with a note that the address is optional and that only the server's operator can reset a forgotten password without one
• Improved the email question and the Email menu with a line that this server uses the address only for password resets and account notices and never sells or shares it, except with the mail service that delivers them

### Classes and Old Gods

• Fixed Probability Shift to lower the target's hit and critical chance as its text says, instead of confusing and slowing it
• Fixed Echo of Tomorrow to ignore half the target's armor instead of adding a flat bonus on top
• Fixed Paradox Collapse's text to name its bonus damage
• Fixed Maelketh, Veloura, Thorgrim and Noctura applying Doom in phase 3, the betrayal fight included; Doom starts at Aurelion
• Fixed Manwe's Shadow Incarnate message to say Manwe heals from the damage it deals
• Fixed Manwe's named abilities ignoring your difficulty setting, Shield Wall Formation, flee guarding, Divine Mandate and Death's Embrace

### Website

• Improved the website with a forgot password page, a new password page and an address confirmation page
• Improved the roadmap with Next and Recently shipped lists, each upcoming item placed in the order it comes
• Improved the home page with a line on the free game and the Steam version
• Fixed the live counters showing 0 when the stats did not load
• Adjusted the reset pages on a server that does not send mail to say so and keep no request

### Online server

• Adjusted the online server to turn off permanent death for a few weeks while the new difficulty curves are measured

### Running your own server

• Improved servers that run their own website with a site address setting for the reset links and the game's reset pointer
• Fixed the account pages answering not found when the web service serves every page, as in the Docker setup
• Fixed the server update script leaving out the game text in every language
• Adjusted the example game server service file to name a placeholder admin for the operator to replace

### Documentation

• Improved the server, Docker and BBS guides with the optional password reset by email and how to turn it on
• Improved the privacy policy to say an email address is never sold or shared, and to name the mail service that delivers password reset mail
• Fixed the privacy policy's combat data section to describe what the game sends and keeps
