import { Component, inject, signal } from '@angular/core';
import { FormBuilder, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.group({
    email:         ['', [Validators.required, Validators.email]],
    commanderName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50)]],
    password:      ['', [Validators.required, Validators.minLength(8)]]
  });

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isLoading.set(true);
    this.error.set(null);

    const { email, commanderName, password } = this.form.getRawValue();

    this.authService.register({
      email: email!,
      commanderName: commanderName!,
      password: password!
    }).subscribe({
      next: () => this.router.navigate(['/bridges']),
      error: () => {
        this.error.set('Inscription impossible — email déjà utilisé');
        this.isLoading.set(false);
      }
    });
  }
}
