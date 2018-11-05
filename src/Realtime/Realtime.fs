module Realtime

open System
open PriceProxy
open Akkling
open Akka.Actor
open Akka.Routing

type Ticker = string

type SubscriberMsg =
    | RequestSubscribe of Ticker list
    | RequestActiveSubscriptionList
    | ActiveSubscriptionsReceived of Ticker list
    | AddSubscriptions of Ticker list
    | PriceReceived of PriceRecord
    | GetRoutees of GetRoutees
    

type PriceSubscriberState = {
    OutstandingRequests : int
    ActiveSubscriptions : Ticker list
    Broadcaster: IActorRef<SubscriberMsg> option
    TickersPendingToAdd : Ticker list
    PriceProxy : PriceProxy
}
with 
    static member Init priceProxy = {
        OutstandingRequests = 0
        ActiveSubscriptions = []
        Broadcaster = None
        TickersPendingToAdd = []
        PriceProxy = priceProxy }



let priceSubscriberReady (state:PriceSubscriberState) (config:Akka.Configuration.Config) (context: Actor<SubscriberMsg>) =

    let broadcast = spawn context "broadcaster" {(props Behaviors.ignore) with Router = Some (upcast new BroadcastGroup(config)) }
    let state' = {state with Broadcaster = Some broadcast}    

    let rec ready state = actor {
        let! msg = context.Receive ()       
        match msg with
        
        | RequestSubscribe tickers ->
            printfn "Request subscribe: %s" (String.Join (", ", tickers))
            let outstanding = state.Broadcaster.Value.Underlying.Ask<Routees>(new GetRoutees()).Result.Members |> Seq.length
            printfn "outstanding: %d" outstanding
            let state' = 
                {state with 
                    OutstandingRequests = outstanding
                    TickersPendingToAdd = tickers}
            state.Broadcaster.Value <! RequestActiveSubscriptionList   
            return! waitingForSubscriptionList state'          

        | RequestActiveSubscriptionList -> 
            printfn "Request active subscription list"
            context.Sender() <! ActiveSubscriptionsReceived state.ActiveSubscriptions

        | AddSubscriptions tickers -> 
            printfn "Add subscriptions: %s" (String.Join (", ", tickers))
            let self = context.Self
            let proxy' = state.PriceProxy |> PriceProxy.subscribeMany tickers (fun price -> 
                self <! PriceReceived price) 
            return! ready { state with PriceProxy = proxy'; ActiveSubscriptions = state.ActiveSubscriptions @ tickers }   

        | PriceReceived price -> 
            printfn "Price %s: %f" price.Ticker price.Value        

        | _ -> printfn "this is fishy"  
        return! ready state }
    
    and waitingForSubscriptionList state = actor {
        let! msg = context.Receive ()
        match msg with 
        | RequestSubscribe _ -> 
            printfn "Request subscribe"
            context.Stash () 

        | RequestActiveSubscriptionList ->
            printfn "Request received for active subscription list"
            context.Sender() <! ActiveSubscriptionsReceived state.ActiveSubscriptions

        | ActiveSubscriptionsReceived activeTickers -> 
            printfn "Received active subs from another aktor: %s" (String.Join(", ", activeTickers))

            let tickers' = state.TickersPendingToAdd |> List.filter (fun t -> not (activeTickers |> List.contains t))
            
            if state.OutstandingRequests = 1 then
                if tickers' |> List.length > 0 then
                    context.Self <! AddSubscriptions tickers'
                return! ready {state with OutstandingRequests = 0; TickersPendingToAdd = []}
            
            else return! waitingForSubscriptionList 
                    {state with 
                        OutstandingRequests = (state.OutstandingRequests - 1)
                        TickersPendingToAdd = tickers'}

        | PriceReceived price -> 
            printfn "Price %s: %f" price.Ticker price.Value                            

        | _ -> printfn "this is fishy"  

        return! waitingForSubscriptionList state }

    ready state' 


[<EntryPoint>]
let main argv =
    let port = 
        match argv with
        | [|strPort|] -> 
            match Int32.TryParse strPort with
            | true, p -> p
            | _ -> 0
        | _ -> 0

    printfn "Port: %d" port
    let configText = (System.IO.File.ReadAllText "realtime.hocon").Replace ("{port}", port.ToString())
    printfn "%s" configText
    let config = Configuration.parse configText
    
    use system = System.create "realtime" config

    let proxy = 
        newProxy ()
        |> definePrice "ticker1" { MinValue= 1.0; MaxValue = 3.0; MinInterval = 1000.0; MaxInterval = 2000.0}
        |> definePrice "ticker2" { MinValue= 100.0; MaxValue = 300.0; MinInterval = 2000.0; MaxInterval = 4000.0}
        |> start
        |> subscribeAll (fun price -> printfn "Generated price: %s: %f" price.Ticker price.Value)

    let state = PriceSubscriberState.Init proxy
    let subscriber = spawn system "pricesub" <| props (priceSubscriberReady state config)

    let scheduler = spawn system "scheduler" {(props Behaviors.ignore) with Router = Some (upcast new RoundRobinGroup(config)) }

    let mutable input = Console.ReadLine () 

    while input <> "" do
        scheduler <! RequestSubscribe [input]
        input <- Console.ReadLine ()
    proxy.Timer.Dispose()    
    CoordinatedShutdown.Get(system).Run().Wait() 
    0 
