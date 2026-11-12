#include "FFTSHUD.h"
#include "CanvasItem.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "EngineUtils.h"
#include "FFTSFightDirector.h"

void AFFTSHUD::Bar(float X, float Y, float W, float H, float Fraction, const FLinearColor& Color, bool bRightToLeft)
{
	Fraction = FMath::Clamp(Fraction, 0.f, 1.f);
	DrawRect(FLinearColor(0.f, 0.f, 0.f, 0.6f), X - 2, Y - 2, W + 4, H + 4);
	const float Fill = W * Fraction;
	DrawRect(Color, bRightToLeft ? X + W - Fill : X, Y, Fill, H);
}

void AFFTSHUD::Text(const FString& S, float X, float Y, const FLinearColor& Color, float Scale)
{
	if (!Canvas) return;
	FCanvasTextItem Item(FVector2D(X, Y), FText::FromString(S), GEngine->GetMediumFont(), Color);
	Item.Scale = FVector2D(Scale, Scale);
	Item.EnableShadow(FLinearColor::Black);
	Canvas->DrawItem(Item);
}

void AFFTSHUD::DrawHUD()
{
	Super::DrawHUD();
	if (!Canvas) return;
	AFFTSFightDirector* D = nullptr;
	for (TActorIterator<AFFTSFightDirector> It(GetWorld()); It; ++It) { D = *It; break; }
	const float W = Canvas->SizeX, H = Canvas->SizeY;
	if (!D) { Text(TEXT("No fight director in this level."), 40, 40, FLinearColor::Red); return; }
	const FString Status = D->ContentStatus();
	if (!Status.IsEmpty()) Text(Status, 40, H - 80, FLinearColor::Red, 1.2f);
	const ffts::CombatSimulation* Sim = D->Sim();
	if (!Sim) return;

	const float BarW = W * 0.36f, Top = 40.f;
	const ffts::Fighter& A = *Sim->A; const ffts::Fighter& B = *Sim->B;
	Bar(40, Top, BarW, 22, A.health / A.maxHealth, FLinearColor(0.85f, 0.15f, 0.12f), false);
	Bar(40, Top + 30, BarW * 0.8f, 8, A.stamina / A.maxStamina, FLinearColor(0.2f, 0.7f, 0.3f), false);
	Bar(40, Top + 44, BarW * 0.8f, 8, A.momentum / 100.f, FLinearColor(0.95f, 0.7f, 0.1f), false);
	Bar(W - 40 - BarW, Top, BarW, 22, B.health / B.maxHealth, FLinearColor(0.85f, 0.15f, 0.12f), true);
	Bar(W - 40 - BarW * 0.8f, Top + 30, BarW * 0.8f, 8, B.stamina / B.maxStamina, FLinearColor(0.2f, 0.7f, 0.3f), true);
	Bar(W - 40 - BarW * 0.8f, Top + 44, BarW * 0.8f, 8, B.momentum / 100.f, FLinearColor(0.95f, 0.7f, 0.1f), true);
	Text(TEXT("YOU"), 40, Top + 58, FLinearColor::White, 1.1f);
	Text(D->PlayerStyleLabel(), 40, Top + 80, FLinearColor(1.f, 0.85f, 0.4f));
	Text(D->OpponentName().ToUpper(), W - 40 - BarW, Top + 58, FLinearColor::White, 1.1f);
	Text(D->OpponentStyleLabel(), W - 40 - BarW, Top + 80, FLinearColor(1.f, 0.85f, 0.4f));
	const int32 Secs = FMath::Max(0, (Sim->cfg.roundTicks - Sim->tick) / 60);
	Text(FString::Printf(TEXT("%d"), Secs), W * 0.5f - 18, Top - 4, FLinearColor::White, 1.6f);
	if (A.momentum >= 100.f) Text(TEXT("FINISHER READY (O)"), 40, Top + 102, FLinearColor(1.f, 0.6f, 0.1f));

	float Y = H * 0.62f;
	for (const FString& Line : D->Feed()) { Text(Line, 40, Y, FLinearColor(0.9f, 0.9f, 0.9f)); Y += 22; }

	if (Sim->isOver())
	{
		const bool Won = Sim->result.winnerId == A.id;
		const FString Banner = Sim->result.method == "Draw" ? TEXT("DRAW") : (Won ? TEXT("YOU WIN") : TEXT("YOU LOSE"));
		Text(Banner, W * 0.5f - 90, H * 0.35f, Won ? FLinearColor(1.f, 0.85f, 0.2f) : FLinearColor(0.9f, 0.2f, 0.2f), 3.f);
		Text(TEXT("R rematch    N next opponent"), W * 0.5f - 130, H * 0.35f + 70, FLinearColor::White, 1.1f);
	}
	if (!D->HasRig()) Text(TEXT("Fight rig not imported yet: run Content/Python/ffts_import_fight_rig.py (Tools > Execute Python Script)"), 40, H - 56, FLinearColor(1.f, 0.6f, 0.2f));
	Text(TEXT("A/D move  Shift block  J light  K heavy  L kick  U grab  Space dodge  I counter  O finisher  T taunt   |   Tab/Q your styles  V opp style  N next  R restart"),
		40, H - 30, FLinearColor(0.75f, 0.75f, 0.75f), 0.85f);
}
