using FluentResults;

using FluentValidation;

using Server.Actions.Contracts;
using Server.Hubs.Contracts;
using Server.Models;
using Server.Persistence.Contracts;

using static Server.Models.RoundAction;

namespace Server.Actions;

// Parameters required to perform an action in a round
public sealed record ActInRoundParams(
    RoundActionType ActionType,
    RoundActionPayload ActionPayload,
    int? RoundId = null,
    Round? Round = null,
    int? PlayerId = null,
    Player? Player = null
);

// Validator for ActInRoundParams
public class ActInRoundValidator : AbstractValidator<ActInRoundParams>
{
    public ActInRoundValidator()
    {
        RuleFor(p => p.ActionType).NotEmpty();
        RuleFor(p => p.ActionPayload).NotEmpty();
        RuleFor(p => p.RoundId).NotEmpty().When(p => p.Round is null);
        RuleFor(p => p.Round).NotEmpty().When(p => p.RoundId is null);
        RuleFor(p => p.PlayerId).NotEmpty().When(p => p.Player is null);
        RuleFor(p => p.Player).NotEmpty().When(p => p.PlayerId is null);
    }
}

// Action class to perform an action in a round
public class ActInRound(
    IRoundsRepository roundsRepository,
    IPlayersRepository playersRepository,
    IAction<FinishRoundParams, Result<Round>> finishRoundAction,
    IGameHubService gameHubService
) : IAction<ActInRoundParams, Result<Round>>
{
    // Method to perform the action
    public async Task<Result<Round>> PerformAsync(ActInRoundParams actionParams)
    {
        // Validate the action parameters
        var actionValidator = new ActInRoundValidator();
        var actionValidationResult = await actionValidator.ValidateAsync(actionParams);

        // Return validation errors if any
        if (actionValidationResult.Errors.Count != 0)
        {
            return Result.Fail(actionValidationResult.Errors.Select(e => e.ErrorMessage));
        }

        var (actionType, actionPayload, roundId, round, playerId, player) = actionParams;

        // Retrieve the round if not provided
        round ??= await roundsRepository.GetById(roundId!.Value);

        if (round is null)
        {
            Result.Fail($"Round with Id \"{roundId}\" not found.");
        }

        // Retrieve the player if not provided
        player ??= await playersRepository.GetById(playerId!.Value);

        if (player is null)
        {
            Result.Fail($"Player with Id \"{playerId}\" not found.");
        }

        // Check if the player can act in the round
        if (!round!.CanPlayerActIn(player!.Id!.Value))
        {
            return Result.Fail("Player cannot act in this round.");
        }

        // Create the round action
        var roundAction = CreateForType(actionType, player.Id.Value, actionPayload);

        // Add the action to the round
        round.Actions.Add(roundAction);


        // Save the round
        await roundsRepository.SaveRound(round);

        // Finish the round if everybody has played
        if (round.EverybodyPlayed())
        {
            var finishRoundParams = new FinishRoundParams(Round: round);
            var finishRoundResult = await finishRoundAction.PerformAsync(finishRoundParams);


            if (finishRoundResult.IsFailed)
            {
                return Result.Fail(finishRoundResult.Errors);
            }
        }

        // Update the current game state
        await gameHubService.UpdateCurrentGame(gameId: round.GameId);

        return Result.Ok(round);
    }
}
