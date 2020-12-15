import { HttpClient } from '@angular/common/http';
import { Component } from '@angular/core';
import { coerceToArrayBuffer, coerceToBase64Url } from './helper';
import { map, switchMap, tap } from 'rxjs/operators';
import { FormControl, FormGroup } from '@angular/forms';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent {
  title = 'webauthn-client';
  userForm = new FormGroup({
    displayName: new FormControl('Kevin Yang'),
    userName: new FormControl('chgc.tw@gmail.com'),
  });
  constructor(private httpClient: HttpClient) {}

  signup() {
    const body = {
      ...this.userForm.value,
      // attType: 'direct',
      // authType: '', // <empty>, platform, cross-platform
      // userVerification: 'preferred',
    };

    this.httpClient
      .post('/api/Auth/makeCredentialOptions', body)
      .pipe(
        tap((option) => console.log('makeCredentialOptions API:', option)),
        map((option) => this.makeCredentialOptions(option)),
        switchMap((credentialOption) =>
          navigator.credentials.create({
            publicKey: credentialOption,
          })
        ),
        map((newCredential: any) => {
          // Move data into Arrays incase it is super long
          let attestationObject = new Uint8Array(
            newCredential.response.attestationObject
          );
          let clientDataJSON = new Uint8Array(
            newCredential.response.clientDataJSON
          );
          let rawId = new Uint8Array(newCredential.rawId);

          const data = {
            id: newCredential.id,
            rawId: coerceToBase64Url(rawId),
            type: newCredential.type,
            extensions: newCredential.getClientExtensionResults(),
            response: {
              AttestationObject: coerceToBase64Url(attestationObject),
              clientDataJson: coerceToBase64Url(clientDataJSON),
            },
          };

          return data;
        }),
        switchMap((data) =>
          this.httpClient.post('/api/Auth/makeCredential', data)
        )
      )
      .subscribe({
        next: (res) => console.log('makeCredential API:', res),
      });
  }

  login() {
    const data = this.userForm.value;
    this.httpClient
      .post('/api/Auth/assertionOptions', data)
      .pipe(
        tap((option) => console.log('assertionOptions API:', option)),
        map((option: any) => {
          const challenge = option.challenge
            .replace(/-/g, '+')
            .replace(/_/g, '/');
          option.challenge = Uint8Array.from(atob(challenge), (c) =>
            c.charCodeAt(0)
          );

          option.allowCredentials.forEach(function (listItem) {
            var fixedId = listItem.id.replace(/\_/g, '/').replace(/\-/g, '+');
            listItem.id = Uint8Array.from(atob(fixedId), (c) =>
              c.charCodeAt(0)
            );
          });
          return option;
        }),
        switchMap((makeAssertionOptions) =>
          navigator.credentials.get({
            publicKey: makeAssertionOptions,
          })
        ),
        map((credential: any) => {
          let authData = new Uint8Array(credential.response.authenticatorData);
          let clientDataJSON = new Uint8Array(
            credential.response.clientDataJSON
          );
          let rawId = new Uint8Array(credential.rawId);
          let sig = new Uint8Array(credential.response.signature);
          const data = {
            id: credential.id,
            rawId: coerceToBase64Url(rawId),
            type: credential.type,
            extensions: credential.getClientExtensionResults(),
            response: {
              authenticatorData: coerceToBase64Url(authData),
              clientDataJson: coerceToBase64Url(clientDataJSON),
              signature: coerceToBase64Url(sig),
            },
          };
          return data;
        }),
        switchMap((data) =>
          this.httpClient.post('/api/auth/makeAssertion', data)
        )
      )
      .subscribe({
        next: (r: any) => {
          console.log('makeAssertion API:', r);
        },
      });
  }

  makeCredentialOptions(option) {
    // Turn the challenge back into the accepted format of padded base64
    option.challenge = coerceToArrayBuffer(option.challenge);
    // Turn ID into a UInt8Array Buffer for some reason
    option.user.id = coerceToArrayBuffer(option.user.id);

    option.excludeCredentials = option.excludeCredentials.map((c) => {
      c.id = coerceToArrayBuffer(c.id);
      return c;
    });

    if (option.authenticatorSelection.authenticatorAttachment === null) {
      option.authenticatorSelection.authenticatorAttachment = undefined;
    }
    return option;
  }
}
