﻿namespace Todo

open System
open Xamarin.Forms
open Tamarin

type TodoItemCell() as this = 
    inherit ViewCell()

    let label = Label(YAlign = TextAlignment.Center)

    let tick = Image(Source = FileImageSource.FromFile ("check.png"))

    let layout = 
        StackLayout(
            Padding = new Thickness(20., 0., 0., 0.),
            Orientation = StackOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.StartAndExpand
        )

    do
        layout.Children.AddRange( label, tick) 
        this.View <- layout

    member __.Name = label
    member __.Done = tick

type TodoListPage() as this = 
    inherit ContentPage()

    let listView = ListView( RowHeight = 40)

    do
        this.Title <- "Todo"
        NavigationPage.SetHasNavigationBar (this, true)

        listView.ItemTemplate <- DataTemplate typeof<TodoItemCell>

        // HACK: workaround issue #894 for now
        if (Device.OS = TargetPlatform.iOS)
        then
            listView.ItemsSource <- [| Activator.CreateInstance<TodoItem>() |]

        let layout = StackLayout()
        if (Device.OS = TargetPlatform.WinPhone)  // WinPhone doesn't have the title showing
        then
            layout.Children.Add <| Label( Text="Todo", Font = Font.SystemFontOfSize(NamedSize.Large))
        layout.Children.Add(listView)
        layout.VerticalOptions <- LayoutOptions.FillAndExpand
        this.Content <- layout
    
    let tbi =
        let nothing = Action ignore
        match Device.OS with
        | TargetPlatform.iOS -> ToolbarItem("+", null, nothing, enum 0, 0)
        | TargetPlatform.Android -> ToolbarItem ("+", "plus", nothing, enum 0, 0)
        | TargetPlatform.WinPhone -> ToolbarItem("Add", "add.png", nothing, enum 0, 0)
        | _ -> null

    do
        this.ToolbarItems.Add (tbi)

    let tbi2 = 
            if Device.OS = TargetPlatform.iOS
            then
                let activated = Action(fun() -> ())
                let tbi2 = ToolbarItem("?", null, activated, enum 0, 0)
                Some tbi2
            else
                None

    do
        tbi2 |> Option.iter this.ToolbarItems.Add 

    member __.TasksListView = listView
    member __.PlusToolBarItem = tbi
    member __.QuestionMarkToolBarItem = tbi2

type TodoListModel() =
    inherit Model() 

    let mutable items = Array.empty<TodoItem>
    let mutable selectedTask = Unchecked.defaultof<TodoItem>

    member this.Items 
        with get() = items
        and set value = 
            items <- value
            this.NotifyPropertyChanged <@ this.Items @>

    member this.SelectedTask 
        with get() = selectedTask
        and set value = 
            selectedTask <- value
            this.NotifyPropertyChanged <@ this.SelectedTask @>

 type TodoListEvents =
    | Refresh 
    | ShowTaskDetails
    | AddTask
    | ReadOutAllTasks

 type TodoListView() = 
    inherit View<TodoListEvents, TodoListModel, TodoListPage>(root = TodoListPage())

    override this.SetBindings model = 
        this.Root.TasksListView.SetBindings(
            itemsSource = <@ model.Items @>, 
            selectedItem = <@ model.SelectedTask @>,
            itemBindings = fun (itemTemplate: TodoItemCell) model ->
                <@@
                    itemTemplate.Name.Text <- model.Name
                    itemTemplate.Done.IsVisible <- model.Done
                @@>
        )

    override this.EventStreams = 
        [
            yield this.Root.Appearing |> Observable.mapTo Refresh
            yield this.Root.TasksListView.ItemSelected |> Observable.mapTo ShowTaskDetails
            yield this.Root.PlusToolBarItem.Activated |> Observable.mapTo AddTask
            yield! 
                this.Root.QuestionMarkToolBarItem
                |> Option.map (fun x -> 
                    x.Activated |> Observable.mapTo ReadOutAllTasks)
                |> Option.toList
        ]

type TodoListController( conn, textToSpeech) =
    inherit Controller<TodoListEvents, TodoListModel>()

    let database = Database( conn)

    override this.InitModel model = 
        model.Items <- database.GetItems() |> Seq.toArray 

    override this.Dispatcher = function
        | Refresh -> Sync this.InitModel
        | ShowTaskDetails -> Async this.ShowTaskDetails 
        | AddTask -> Async this.AddTask 
        | ReadOutAllTasks -> Sync this.ReadOutAllTasks 

    member this.ShowTaskDetails model =
        async {
            let view = TodoItemView()
            let controller = TodoItemController( conn, textToSpeech)
            let mvc = Mvc(model.SelectedTask, view, controller)
            let eventLoop = mvc.Start()
            do! this.Navigation.PushAsync(page = downcast view.Root) |> Async.AwaitIAsyncResult |> Async.Ignore
        }

    member this.AddTask model =
        async {
            let model = Activator.CreateInstance()
            let view = TodoItemView()
            let controller = TodoItemController( conn, textToSpeech)
            let mvc = Mvc(model, view, controller)
            let eventLoop = mvc.Start()
            do! this.Navigation.PushAsync(page = downcast view.Root) |> Async.AwaitIAsyncResult |> Async.Ignore
        }

    member this.ReadOutAllTasks model =
        query {
            for task in database.GetItems() do
            where (not task.Done)
            select task.Name
        }
        |> fun xs -> Linq.Enumerable.DefaultIfEmpty( xs,  "there are no tasks to do")
        |> String.concat " "
        |> textToSpeech.Speak
    
    