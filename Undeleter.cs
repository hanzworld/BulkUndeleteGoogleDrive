using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Google.Apis.Drive.v3.Data;
using Serilog;

namespace BulkUndeleteGoogleDrive
{
    public interface IUndeleter
    {
        void UndeleteAfter(DateTimeOffset restoreFilesDeletedAfter);
    }
    public class Undeleter : IUndeleter
    {
        private readonly GoogleDriveService drive;
        private DateTimeOffset restoreFilesDeletedAfter;

        public Undeleter(GoogleDriveService drive)
        {
            this.drive = drive;
        }
        public void UndeleteAfter(DateTimeOffset restoreFilesDeletedAfter)
        {
            //yeah I know, should be injected
            this.restoreFilesDeletedAfter = restoreFilesDeletedAfter;

            Console.WriteLine("What date do you want files restored from?");
            Console.WriteLine(restoreFilesDeletedAfter);

            var token = drive.Changes.GetStartPageToken().Execute().StartPageTokenValue;
            if (token == null)
            {
                return;
            }
            int earliestPage = FindPageWithLastRelevantChangeRecord(0, int.Parse(token));

            List<Change> changes = GetChangesFrom(earliestPage);

            //apply some filtering because the endpoint doesn't let us do that
            Console.WriteLine(changes.Count);

            //exclude ones before the time we care about
            //make sure they are trashed
            //and we want to process them in reverse order of how they were deleted
            var newList = changes.Where(c =>
                c.Time.GetValueOrDefault() >= restoreFilesDeletedAfter &&
                c.File != null && c.File.Trashed.GetValueOrDefault()
                ).OrderByDescending(c => c.Time).ToList();

            Console.WriteLine(newList.Count);


            newList.ForEach(c => UnTrashFile(c.File));

            Console.WriteLine(this);
        }

        private List<Change> GetChangesFrom(int earliestPage)
        {
            Console.WriteLine($"Retrieving change history from page {earliestPage} until now");
            var changesCollection = new List<Change>();
            var nextPage = earliestPage.ToString();
            while (nextPage != null)
            {
                //get all the changes - we can bump up the pageSize to get data in bulk now
                var page = GetPageOfChanges(nextPage, 500, true);
                Console.WriteLine($"Retrieved {nextPage} to {page.NextPageToken}");
                nextPage = page.NextPageToken;
                changesCollection.AddRange(page.Changes);
            }
            return changesCollection;
        }

        //Yep, it's a good ol' fashioned binary search instead of looping over all pages
        private int FindPageWithLastRelevantChangeRecord(int earliestPage, int latestPage)
        {
            int middlePage = (earliestPage + latestPage) / 2;
            ChangeList changesResponse = GetPageOfChanges(middlePage.ToString(), 20);

            //Google return it in RFC3339 - and I confirmed their client does in fact cast it to the local system time - so this is in the right time.
            var thisPageOldestRecord = changesResponse.Changes.Min(c => c.Time).Value;
            var thisPageNewestRecord = changesResponse.Changes.Max(c => c.Time).Value;
            Console.WriteLine($"Pages {middlePage} ranges from {thisPageOldestRecord} to {thisPageNewestRecord}");

            //if this page contains our target date, then we are done!
            if (thisPageOldestRecord < restoreFilesDeletedAfter && thisPageNewestRecord >= restoreFilesDeletedAfter)
            {
                Console.WriteLine($"Identified page {middlePage} as the start point for records of interest");
                return middlePage;
            }

            //if this page only contains records more recent than our target date, then keep doing down the rabbit hole Alice!
            if (thisPageOldestRecord > restoreFilesDeletedAfter)
            {
                return FindPageWithLastRelevantChangeRecord(earliestPage, middlePage);
            }

            //if this page only contains records older our target date, then we've overshot
            if (thisPageOldestRecord < restoreFilesDeletedAfter)
            {
                return FindPageWithLastRelevantChangeRecord(middlePage, latestPage);
            }
            Console.WriteLine("Shouldn't be here, methinks you made a coding mistake");
            Console.WriteLine($"Earliest : {earliestPage}");
            Console.WriteLine($"Latest : {latestPage}");
            throw new Exception();
        }

        private ChangeList GetPageOfChanges(string pageNumber, int pageSize, bool withDetail)
        {

            var changesRequest = drive.Changes.List(pageNumber);
            changesRequest.PageSize = pageSize;
            changesRequest.IncludeRemoved = true;
            if (withDetail)
            {
                changesRequest.Fields = "nextPageToken,changes(kind,changeType,time,removed,file(id,name,mimeType,trashed,explicitlyTrashed))";
            }
            var changesResponse = changesRequest.Execute();
            return changesResponse;
        }
        private ChangeList GetPageOfChanges(string pageNumber, int pageSize)
        {
            return GetPageOfChanges(pageNumber, pageSize, withDetail: false);
        }

        private void UnTrashFile(File file)
        {

            //Google API only takes fields you want to modify - so don't resubmit thte whole object
            var cleanFile = new File();
            cleanFile.Trashed = false;

            var untrashRequest = drive.Files.Update(cleanFile, file.Id);

            try
            {

                var untrashResponse = untrashRequest.Execute();
                Log.Warning("Untrashed {fileName} ({id})", file.Name, untrashRequest.FileId);
            }
            catch (Exception e)
            {
                Log.Error("Failed to untrash {fileName} ({id}) : {e}", file.Name, untrashRequest.FileId, e);
            }



        }
    }
}