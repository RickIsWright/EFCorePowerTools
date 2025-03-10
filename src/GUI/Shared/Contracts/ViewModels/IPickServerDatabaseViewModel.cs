﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using EFCorePowerTools.Common.Models;
using EFCorePowerTools.Contracts.EventArgs;
using RevEng.Common;

namespace EFCorePowerTools.Contracts.ViewModels
{
    public interface IPickServerDatabaseViewModel : IViewModel
    {
        event EventHandler<CloseRequestedEventArgs> CloseRequested;

        ICommand LoadedCommand { get; }
        ICommand AddDatabaseConnectionCommand { get; }
        ICommand OkCommand { get; }
        ICommand CancelCommand { get; }

        ObservableCollection<DatabaseConnectionModel> DatabaseConnections { get; }
        ObservableCollection<DatabaseDefinitionModel> DatabaseDefinitions { get; }

        List<SchemaInfo> Schemas { get; }

        DatabaseConnectionModel SelectedDatabaseConnection { get; set; }
        DatabaseDefinitionModel SelectedDatabaseDefinition { get; set; }

        string UiHint { get; set; }
        int CodeGenerationMode { get; set; }
        bool FilterSchemas { get; set; }
    }
}